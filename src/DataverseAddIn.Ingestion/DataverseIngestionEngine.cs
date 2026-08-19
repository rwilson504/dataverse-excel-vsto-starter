using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

namespace DataverseAddIn.Ingestion
{
    /// <summary>
    /// Pushes records into a Dataverse table at the highest throughput the environment allows.
    /// </summary>
    /// <remarks>
    /// Deliberately depends only on <see cref="IOrganizationService"/> so it can be unit tested
    /// with a fake and reused from any host. The caller supplies a factory that produces one
    /// service per worker thread — with ServiceClient that means <c>Clone()</c>.
    /// <para>
    /// This method blocks. Call it from a background thread; never from a UI thread.
    /// </para>
    /// </remarks>
    public sealed class DataverseIngestionEngine
    {
        private readonly Func<IOrganizationService> _serviceFactory;
        private readonly int _recommendedDegreesOfParallelism;
        private readonly IngestionOptions _options;

        public DataverseIngestionEngine(
            Func<IOrganizationService> serviceFactory,
            int recommendedDegreesOfParallelism,
            IngestionOptions options = null)
        {
            _serviceFactory = serviceFactory ?? throw new ArgumentNullException(nameof(serviceFactory));

            if (recommendedDegreesOfParallelism < 1)
                throw new ArgumentOutOfRangeException(nameof(recommendedDegreesOfParallelism));

            _recommendedDegreesOfParallelism = recommendedDegreesOfParallelism;
            _options = options ?? new IngestionOptions();
            _options.Validate();
        }

        public IngestionResult Ingest(
            string tableLogicalName,
            IReadOnlyList<Entity> records,
            IProgress<IngestionProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(tableLogicalName))
                throw new ArgumentException("Table logical name is required.", nameof(tableLogicalName));
            if (records == null) throw new ArgumentNullException(nameof(records));

            var stopwatch = Stopwatch.StartNew();
            var strategy = ResolveStrategy(tableLogicalName);

            // Never exceed the environment's hint: going above it reduces throughput.
            var dop = Math.Min(
                _options.MaxDegreeOfParallelism ?? _recommendedDegreesOfParallelism,
                _recommendedDegreesOfParallelism);

            var chunkSize = _options.ChunkSizeFor(strategy);
            var chunks = Chunk(records, chunkSize).ToList();

            var errors = new ConcurrentBag<IngestionError>();
            var createdIds = new ConcurrentBag<Guid>();
            var succeeded = 0;
            var throttledRetries = 0;

            try
            {
                Parallel.ForEach(
                    chunks,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = dop,
                        CancellationToken = cancellationToken
                    },
                    _serviceFactory,
                    (chunk, state, index, service) =>
                    {
                        var outcome = SendChunk(service, tableLogicalName, chunk, strategy, ref throttledRetries, cancellationToken);

                        foreach (var id in outcome.CreatedIds) createdIds.Add(id);
                        foreach (var error in outcome.Errors) errors.Add(error);

                        Interlocked.Add(ref succeeded, outcome.Succeeded);

                        if (outcome.Errors.Count > 0 && !_options.ContinueOnError)
                            state.Stop();

                        progress?.Report(new IngestionProgress(
                            Volatile.Read(ref succeeded), errors.Count, records.Count));

                        return service;
                    },
                    service => (service as IDisposable)?.Dispose());
            }
            catch (OperationCanceledException)
            {
                // Report what completed rather than losing the partial result.
            }

            stopwatch.Stop();

            return new IngestionResult(
                strategy,
                succeeded,
                errors.OrderBy(e => e.RecordIndex).ToList(),
                createdIds.ToList(),
                stopwatch.Elapsed,
                dop,
                throttledRetries);
        }

        private IngestionStrategy ResolveStrategy(string tableLogicalName)
        {
            if (_options.Strategy != IngestionStrategy.Auto)
                return _options.Strategy;

            var message = BulkMessageSupport.MessageNameFor(_options.Operation);

            var service = _serviceFactory();

            try
            {
                return BulkMessageSupport.IsMessageAvailable(service, tableLogicalName, message)
                    ? IngestionStrategy.BulkMessages
                    : IngestionStrategy.Batch;
            }
            finally
            {
                (service as IDisposable)?.Dispose();
            }
        }

        private ChunkOutcome SendChunk(
            IOrganizationService service,
            string tableLogicalName,
            Chunked chunk,
            IngestionStrategy strategy,
            ref int throttledRetries,
            CancellationToken cancellationToken)
        {
            try
            {
                switch (strategy)
                {
                    case IngestionStrategy.BulkMessages:
                        return SendBulk(service, tableLogicalName, chunk, ref throttledRetries, cancellationToken);
                    case IngestionStrategy.Batch:
                        return SendBatch(service, chunk, ref throttledRetries, cancellationToken);
                    default:
                        return SendIndividually(service, chunk, ref throttledRetries, cancellationToken);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                // A bulk request on a standard table rolls back entirely, so the failure says
                // nothing about which record was bad. Re-send one at a time to find out.
                if (strategy == IngestionStrategy.BulkMessages && _options.IsolateFailuresInFailedChunks && chunk.Count > 1)
                    return SendIndividually(service, chunk, ref throttledRetries, cancellationToken);

                return ChunkOutcome.AllFailed(chunk, ex);
            }
        }

        private ChunkOutcome SendBulk(
            IOrganizationService service,
            string tableLogicalName,
            Chunked chunk,
            ref int throttledRetries,
            CancellationToken cancellationToken)
        {
            var collection = new EntityCollection(chunk.Records.ToList()) { EntityName = tableLogicalName };

            OrganizationRequest request;

            switch (_options.Operation)
            {
                case IngestionOperation.Create:
                    request = new CreateMultipleRequest { Targets = collection };
                    break;
                case IngestionOperation.Update:
                    request = new UpdateMultipleRequest { Targets = collection };
                    break;
                default:
                    request = new OrganizationRequest("UpsertMultiple")
                    {
                        Parameters = { { "Targets", collection } }
                    };
                    break;
            }

            ApplyBypass(request);

            var response = ServiceProtection.Execute(
                () => service.Execute(request), _options.MaxRetriesPerChunk, ref throttledRetries, cancellationToken);

            var ids = response is CreateMultipleResponse createResponse
                ? (IReadOnlyList<Guid>)createResponse.Ids
                : Array.Empty<Guid>();

            return new ChunkOutcome(chunk.Count, ids, Array.Empty<IngestionError>());
        }

        private ChunkOutcome SendBatch(
            IOrganizationService service,
            Chunked chunk,
            ref int throttledRetries,
            CancellationToken cancellationToken)
        {
            var request = new ExecuteMultipleRequest
            {
                Settings = new ExecuteMultipleSettings { ContinueOnError = true, ReturnResponses = true },
                Requests = new OrganizationRequestCollection()
            };

            foreach (var record in chunk.Records)
                request.Requests.Add(BuildSingleRequest(record));

            ApplyBypass(request);

            var response = (ExecuteMultipleResponse)ServiceProtection.Execute(
                () => service.Execute(request), _options.MaxRetriesPerChunk, ref throttledRetries, cancellationToken);

            var ids = new List<Guid>();
            var errors = new List<IngestionError>();

            foreach (var item in response.Responses)
            {
                var recordIndex = chunk.StartIndex + item.RequestIndex;

                if (item.Fault != null)
                {
                    errors.Add(new IngestionError(
                        recordIndex,
                        chunk.Records[item.RequestIndex],
                        new InvalidOperationException(item.Fault.Message)));
                }
                else if (item.Response is CreateResponse created)
                {
                    ids.Add(created.id);
                }
            }

            return new ChunkOutcome(chunk.Count - errors.Count, ids, errors);
        }

        private ChunkOutcome SendIndividually(
            IOrganizationService service,
            Chunked chunk,
            ref int throttledRetries,
            CancellationToken cancellationToken)
        {
            var ids = new List<Guid>();
            var errors = new List<IngestionError>();

            for (var i = 0; i < chunk.Count; i++)
            {
                var record = chunk.Records[i];
                var recordIndex = chunk.StartIndex + i;

                try
                {
                    var request = BuildSingleRequest(record);
                    ApplyBypass(request);

                    var response = ServiceProtection.Execute(
                        () => service.Execute(request), _options.MaxRetriesPerChunk, ref throttledRetries, cancellationToken);

                    if (response is CreateResponse created) ids.Add(created.id);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    errors.Add(new IngestionError(recordIndex, record, ex));
                }
            }

            return new ChunkOutcome(chunk.Count - errors.Count, ids, errors);
        }

        private OrganizationRequest BuildSingleRequest(Entity record)
        {
            switch (_options.Operation)
            {
                case IngestionOperation.Create: return new CreateRequest { Target = record };
                case IngestionOperation.Update: return new UpdateRequest { Target = record };
                default: return new UpsertRequest { Target = record };
            }
        }

        private void ApplyBypass(OrganizationRequest request)
        {
            if (_options.BypassCustomPluginExecution)
                request["BypassCustomPluginExecution"] = true;
        }

        private static IEnumerable<Chunked> Chunk(IReadOnlyList<Entity> records, int size)
        {
            for (var start = 0; start < records.Count; start += size)
            {
                var count = Math.Min(size, records.Count - start);
                var slice = new Entity[count];

                for (var i = 0; i < count; i++) slice[i] = records[start + i];

                yield return new Chunked(start, slice);
            }
        }

        private sealed class Chunked
        {
            public Chunked(int startIndex, IReadOnlyList<Entity> records)
            {
                StartIndex = startIndex;
                Records = records;
            }

            public int StartIndex { get; }

            public IReadOnlyList<Entity> Records { get; }

            public int Count => Records.Count;
        }

        private sealed class ChunkOutcome
        {
            public ChunkOutcome(int succeeded, IReadOnlyList<Guid> createdIds, IReadOnlyList<IngestionError> errors)
            {
                Succeeded = succeeded;
                CreatedIds = createdIds;
                Errors = errors;
            }

            public int Succeeded { get; }

            public IReadOnlyList<Guid> CreatedIds { get; }

            public IReadOnlyList<IngestionError> Errors { get; }

            public static ChunkOutcome AllFailed(Chunked chunk, Exception exception)
            {
                var errors = new List<IngestionError>(chunk.Count);

                for (var i = 0; i < chunk.Count; i++)
                    errors.Add(new IngestionError(chunk.StartIndex + i, chunk.Records[i], exception));

                return new ChunkOutcome(0, Array.Empty<Guid>(), errors);
            }
        }
    }
}

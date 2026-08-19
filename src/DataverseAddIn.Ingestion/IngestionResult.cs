using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace DataverseAddIn.Ingestion
{
    public sealed class IngestionError
    {
        public IngestionError(int recordIndex, Entity record, Exception exception)
        {
            RecordIndex = recordIndex;
            Record = record;
            Exception = exception;
        }

        /// <summary>Index in the list passed to the engine, so it maps back to a sheet row.</summary>
        public int RecordIndex { get; }

        public Entity Record { get; }

        public Exception Exception { get; }

        public string Message => Exception?.Message ?? "Unknown error.";

        public override string ToString() => $"[{RecordIndex}] {Message}";
    }

    public sealed class IngestionProgress
    {
        public IngestionProgress(int succeeded, int failed, int total)
        {
            Succeeded = succeeded;
            Failed = failed;
            Total = total;
        }

        public int Succeeded { get; }

        public int Failed { get; }

        public int Total { get; }

        public int Processed => Succeeded + Failed;

        public double PercentComplete => Total == 0 ? 100d : Processed * 100d / Total;
    }

    public sealed class IngestionResult
    {
        internal IngestionResult(
            IngestionStrategy strategy,
            int succeeded,
            IReadOnlyList<IngestionError> errors,
            IReadOnlyList<Guid> createdIds,
            TimeSpan elapsed,
            int degreeOfParallelism,
            int throttledRetries)
        {
            Strategy = strategy;
            Succeeded = succeeded;
            Errors = errors;
            CreatedIds = createdIds;
            Elapsed = elapsed;
            DegreeOfParallelism = degreeOfParallelism;
            ThrottledRetries = throttledRetries;
        }

        /// <summary>The strategy actually used, which may differ from the requested one.</summary>
        public IngestionStrategy Strategy { get; }

        public int Succeeded { get; }

        public IReadOnlyList<IngestionError> Errors { get; }

        /// <summary>Populated for create operations using bulk messages or individual requests.</summary>
        public IReadOnlyList<Guid> CreatedIds { get; }

        public TimeSpan Elapsed { get; }

        public int DegreeOfParallelism { get; }

        /// <summary>
        /// Service protection retries. Zero at high volume usually means you are leaving
        /// throughput on the table, not that everything is healthy.
        /// </summary>
        public int ThrottledRetries { get; }

        public int Failed => Errors.Count;

        public double RecordsPerSecond =>
            Elapsed.TotalSeconds <= 0 ? 0 : Succeeded / Elapsed.TotalSeconds;

        public override string ToString() =>
            $"{Strategy}: {Succeeded} succeeded, {Failed} failed in {Elapsed.TotalSeconds:F1}s " +
            $"({RecordsPerSecond:F0}/s, DOP {DegreeOfParallelism}, {ThrottledRetries} throttled)";
    }
}

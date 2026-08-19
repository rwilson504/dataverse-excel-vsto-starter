using System;

namespace DataverseAddIn.Ingestion
{
    /// <summary>How records are sent to Dataverse.</summary>
    public enum IngestionStrategy
    {
        /// <summary>
        /// Probe the table and use <see cref="BulkMessages"/> when available, otherwise
        /// <see cref="Batch"/>.
        /// </summary>
        Auto = 0,

        /// <summary>
        /// CreateMultiple / UpdateMultiple / UpsertMultiple. Highest throughput, but on
        /// standard tables a single bad record rolls back the whole request.
        /// </summary>
        BulkMessages = 1,

        /// <summary>
        /// ExecuteMultiple. Operations run sequentially on the server, so throughput per
        /// operation is no better than individual requests — the win is fewer round trips
        /// plus per-record error reporting.
        /// </summary>
        Batch = 2,

        /// <summary>One request per record. Slowest, but the most precise error reporting.</summary>
        Individual = 3
    }

    public enum IngestionOperation
    {
        Create = 0,
        Update = 1,
        Upsert = 2
    }

    public sealed class IngestionOptions
    {
        public IngestionStrategy Strategy { get; set; } = IngestionStrategy.Auto;

        public IngestionOperation Operation { get; set; } = IngestionOperation.Create;

        /// <summary>
        /// Records per request for <see cref="IngestionStrategy.BulkMessages"/>. These are
        /// processed as one optimized set, so efficiency *increases* with size. Microsoft
        /// suggests 100–1000 for standard tables and 100 for elastic. Too large and you hit
        /// the message size or time limit, which fails the whole request.
        /// </summary>
        public int BulkMessageBatchSize { get; set; } = 200;

        /// <summary>
        /// Records per request for <see cref="IngestionStrategy.Batch"/>. Deliberately small.
        /// ExecuteMultiple runs its operations sequentially on a single server thread, so a
        /// large batch is a long serial run that blocks parallelism. Keeping batches tiny and
        /// raising the degree of parallelism measures faster; the batch only exists to amortize
        /// per-request overhead. Worth re-tuning per environment.
        /// </summary>
        public int ExecuteMultipleBatchSize { get; set; } = 3;

        /// <summary>
        /// Null means use the environment's <c>x-ms-dop-hint</c>. Exceeding that value makes
        /// throughput worse, so only override it downwards.
        /// </summary>
        public int? MaxDegreeOfParallelism { get; set; }

        /// <summary>
        /// Keep going after a failed chunk and report the errors, rather than stopping.
        /// </summary>
        public bool ContinueOnError { get; set; } = true;

        /// <summary>
        /// When a chunk fails under <see cref="IngestionStrategy.BulkMessages"/>, retry its
        /// records individually to find which ones are actually bad. Costs time, but turns an
        /// all-or-nothing rollback into a precise error list.
        /// </summary>
        public bool IsolateFailuresInFailedChunks { get; set; } = true;

        /// <summary>
        /// Skip synchronous plug-ins and other custom logic. Requires the caller to be a
        /// system administrator or hold the bypass privilege.
        /// </summary>
        public bool BypassCustomPluginExecution { get; set; }

        public int MaxRetriesPerChunk { get; set; } = 5;

        internal void Validate()
        {
            if (BulkMessageBatchSize < 1) throw new ArgumentOutOfRangeException(nameof(BulkMessageBatchSize));
            if (ExecuteMultipleBatchSize < 1) throw new ArgumentOutOfRangeException(nameof(ExecuteMultipleBatchSize));
            if (MaxDegreeOfParallelism.HasValue && MaxDegreeOfParallelism < 1)
                throw new ArgumentOutOfRangeException(nameof(MaxDegreeOfParallelism));
            if (MaxRetriesPerChunk < 0) throw new ArgumentOutOfRangeException(nameof(MaxRetriesPerChunk));
        }

        internal int ChunkSizeFor(IngestionStrategy strategy)
        {
            switch (strategy)
            {
                case IngestionStrategy.BulkMessages: return BulkMessageBatchSize;
                case IngestionStrategy.Batch: return ExecuteMultipleBatchSize;
                default: return 1;
            }
        }
    }
}

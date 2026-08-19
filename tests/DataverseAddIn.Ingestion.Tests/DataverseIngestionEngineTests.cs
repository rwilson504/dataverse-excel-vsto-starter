using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Xunit;

namespace DataverseAddIn.Ingestion.Tests
{
    public class DataverseIngestionEngineTests
    {
        private const string Table = "sample_widget";

        private static List<Entity> Records(int count) =>
            Enumerable.Range(0, count)
                .Select(i => new Entity(Table) { ["sample_name"] = $"row {i}" })
                .ToList();

        [Fact]
        public void Auto_uses_bulk_messages_when_the_table_supports_them()
        {
            var fake = new FakeDataverse { BulkMessagesSupported = true };
            var engine = new DataverseIngestionEngine(fake.Factory, 4, new IngestionOptions { BulkMessageBatchSize = 100 });

            var result = engine.Ingest(Table, Records(250));

            Assert.Equal(IngestionStrategy.BulkMessages, result.Strategy);
            Assert.Equal(250, result.Succeeded);
            Assert.Empty(result.Errors);
            Assert.Equal(250, result.CreatedIds.Count);
        }

        [Fact]
        public void Auto_falls_back_to_batch_when_bulk_messages_are_unavailable()
        {
            // Mirrors Account and Contact, which do not support CreateMultiple.
            var fake = new FakeDataverse { BulkMessagesSupported = false };
            var engine = new DataverseIngestionEngine(fake.Factory, 4, new IngestionOptions { ExecuteMultipleBatchSize = 10 });

            var result = engine.Ingest(Table, Records(10));

            Assert.Equal(IngestionStrategy.Batch, result.Strategy);
            Assert.Equal(10, result.Succeeded);
            Assert.Single(fake.RequestsOfType<ExecuteMultipleRequest>());
        }

        [Fact]
        public void ExecuteMultiple_batches_stay_small_by_default_so_parallelism_is_not_serialized()
        {
            var fake = new FakeDataverse { BulkMessagesSupported = false };
            var engine = new DataverseIngestionEngine(fake.Factory, 4);

            engine.Ingest(Table, Records(30));

            var sizes = fake.RequestsOfType<ExecuteMultipleRequest>()
                .Cast<ExecuteMultipleRequest>()
                .Select(r => r.Requests.Count)
                .ToList();

            Assert.Equal(10, sizes.Count);
            Assert.All(sizes, size => Assert.Equal(3, size));
        }

        [Fact]
        public void Records_are_chunked_to_the_configured_batch_size()
        {
            var fake = new FakeDataverse();
            var engine = new DataverseIngestionEngine(fake.Factory, 1, new IngestionOptions { BulkMessageBatchSize = 50 });

            engine.Ingest(Table, Records(120));

            // 120 records at 50 per chunk = 50 + 50 + 20.
            var sizes = fake.RequestsOfType<CreateMultipleRequest>()
                .Cast<CreateMultipleRequest>()
                .Select(r => r.Targets.Entities.Count)
                .OrderByDescending(n => n)
                .ToList();

            Assert.Equal(new[] { 50, 50, 20 }, sizes);
        }

        [Fact]
        public void A_failed_bulk_chunk_is_retried_individually_to_isolate_the_bad_record()
        {
            var fake = new FakeDataverse
            {
                // Standard-table behaviour: one bad record fails the whole request.
                FailBulk = request => request.Targets.Entities.Any(IsPoison)
                    ? new InvalidOperationException("rolled back")
                    : null,
                FailSingle = entity => IsPoison(entity)
                    ? new InvalidOperationException("bad value in sample_name")
                    : null
            };

            var engine = new DataverseIngestionEngine(fake.Factory, 1, new IngestionOptions
            {
                BulkMessageBatchSize = 10,
                IsolateFailuresInFailedChunks = true
            });

            var records = Records(10);
            records[7]["sample_name"] = "poison";

            var result = engine.Ingest(Table, records);

            Assert.Equal(9, result.Succeeded);
            var error = Assert.Single(result.Errors);
            Assert.Equal(7, error.RecordIndex);
        }

        [Fact]
        public void Batch_errors_map_back_to_the_original_record_index()
        {
            var fake = new FakeDataverse
            {
                BulkMessagesSupported = false,
                FailSingle = entity => IsPoison(entity) ? new InvalidOperationException("bad") : null
            };

            var engine = new DataverseIngestionEngine(fake.Factory, 1, new IngestionOptions { ExecuteMultipleBatchSize = 10 });

            var records = Records(30);
            records[23]["sample_name"] = "poison";

            var result = engine.Ingest(Table, records);

            Assert.Equal(29, result.Succeeded);
            // Index 23 sits in the third chunk; the offset must survive chunking.
            Assert.Equal(23, Assert.Single(result.Errors).RecordIndex);
        }

        [Fact]
        public void Degree_of_parallelism_never_exceeds_the_environment_recommendation()
        {
            var fake = new FakeDataverse();
            var engine = new DataverseIngestionEngine(fake.Factory, 8, new IngestionOptions
            {
                MaxDegreeOfParallelism = 64
            });

            var result = engine.Ingest(Table, Records(10));

            Assert.Equal(8, result.DegreeOfParallelism);
        }

        [Fact]
        public void Progress_is_reported_and_totals_reconcile()
        {
            var fake = new FakeDataverse();
            var engine = new DataverseIngestionEngine(fake.Factory, 1, new IngestionOptions { BulkMessageBatchSize = 25 });
            var reports = new List<IngestionProgress>();

            var result = engine.Ingest(Table, Records(100), new Progress<IngestionProgress>(reports.Add));

            Assert.Equal(100, result.Succeeded);
            Assert.All(reports, r => Assert.Equal(100, r.Total));
        }

        private static bool IsPoison(Entity entity) =>
            string.Equals(entity.GetAttributeValue<string>("sample_name"), "poison", StringComparison.Ordinal);
    }
}

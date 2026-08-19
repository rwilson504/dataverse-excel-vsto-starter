# Ingestion

The add-in's purpose is pushing spreadsheet rows into Dataverse as fast as the environment
allows. `DataverseAddIn.Ingestion` owns that, and it depends only on
`Microsoft.CrmSdk.CoreAssemblies` — no `ServiceClient`, no MSAL, no auth. It takes a
`Func<IOrganizationService>` and a degree of parallelism, which is why it is unit testable
offline and would move to an Azure Function unchanged.

## The throughput model

The single most important fact, and the one most often got wrong:

> **`ExecuteMultiple` is not a throughput tool.** Its operations are "applied sequentially on
> the server, so there's no improved efficiency per operation". The benefit is fewer round
> trips and per-item error reporting, not speed.

Throughput comes from two things instead:

1. **Bulk messages** — `CreateMultiple` / `UpdateMultiple` / `UpsertMultiple`, which the
   server processes as one optimized set.
2. **Parallelism** at the environment's recommended degree, read from the `x-ms-dop-hint`
   response header via `ServiceClient.RecommendedDegreesOfParallelism`. Exceeding it makes
   throughput *worse*.

Both multiply. Neither substitutes for the other.

## Strategies

| Strategy | What it uses | When |
| --- | --- | --- |
| `Auto` (default) | Probes, then bulk or batch | Almost always |
| `BulkMessages` | `CreateMultiple` etc. | Highest throughput |
| `Batch` | `ExecuteMultiple` | Tables without bulk support |
| `Individual` | One request per record | Most precise errors, slowest |

**Strategy is probed, not configured.** Bulk messages are unavailable on some standard
tables — including **Account and Contact** — while custom tables generally support them. The
engine queries `sdkmessagefilters` and falls back to `ExecuteMultiple`, so callers do not have
to know which tables qualify.

## Batch size is per strategy, not global

This is counterintuitive and was corrected by field measurement:

```csharp
public int BulkMessageBatchSize { get; set; } = 200;   // large
public int ExecuteMultipleBatchSize { get; set; } = 3; // deliberately tiny
```

- **Bulk messages: large batches.** Processed as one optimized set, so efficiency *increases*
  with size. Microsoft suggests 100–1000 for standard tables, 100 for elastic. Too large and
  you hit the message-size or time limit, which fails the whole request.
- **`ExecuteMultiple`: tiny batches, high parallelism.** Because its operations run
  sequentially on a single server thread, a large batch is a long serial run that starves
  parallelism. Roughly 3 records per request with high concurrency measured faster than large
  batches. Worth re-tuning per environment.

A single `BatchSize` setting would be wrong for one of the two. There are two.

## Failure isolation

On standard tables, **one bad record rolls back the entire bulk request**, and the fault says
nothing about which record was at fault. That is useless to a spreadsheet user.

With `IsolateFailuresInFailedChunks` (default on), a failed chunk is re-sent one record at a
time, converting an opaque rollback into a precise per-row error list. It costs time, and it
only costs it on chunks that actually failed.

Elastic tables allow partial success and do not need this.

## Service protection

Errors under load are expected, not a defect:

> "If you aren't getting some service protection limit errors, you haven't maximized the
> capability of your application."

The engine honours `Retry-After` and surfaces `IngestionResult.ThrottledRetries`. **Zero
retries at volume is a signal to push harder, not a success metric.**

Limits apply *per web server*, which is why the ingestion adapter sets
`EnableAffinityCookie = false` — spreading requests across servers raises the effective
ceiling. That is correct for bulk work and wrong for interactive cached reads, so it is
applied in the ingestion adapter rather than at connection time.

A browser-based client cannot disable the affinity cookie at all, which is a structural
throughput cap and part of why this ships as VSTO rather than a web add-in
([0005](../decisions/0005-platform-choice.md)).

## Using it

```csharp
// Connections supplies the adapter: Clone() per worker, DOP, affinity off.
var engine = serviceClient.CreateEngine(new IngestionOptions
{
    Operation = IngestionOperation.Create,
    ContinueOnError = true
});

var mapper = new SheetMapper("sample_widget", new[]
{
    new ColumnMapping(0, "sample_name"),
    new ColumnMapping(1, "sample_count", SheetValueType.Integer)
});

// Excel hands back a 1-based object[,] from Range.Value2.
var mapped = mapper.Map(worksheetBlock);
var result = engine.Ingest(mapped.Records);
```

`Ingest` **blocks** — .NET Framework has no `Parallel.ForEachAsync`. Hosts must call it from
a background thread. In VSTO that is mandatory: blocking the UI thread deadlocks Excel.

## Scale context

The design target was **20,000 rows worst case**. Against documented limits of 6,000 requests
and 20 minutes of execution per user, per server, per five-minute window, that is roughly 2%
of the request budget — which is why scale-out compute was rejected as solving a problem this
workload does not have. Revisit if volumes grow roughly tenfold.

See [0004](../decisions/0004-ingestion-engine.md) for the full reasoning and
[0005](../decisions/0005-platform-choice.md) for how volume decided the platform.

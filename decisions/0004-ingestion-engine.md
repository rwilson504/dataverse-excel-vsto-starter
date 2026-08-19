# 0004 — Ingestion engine as its own assembly, depending only on Microsoft.Xrm.Sdk

- Status: accepted
- Date: 2026-08-18
- Extends [0003](0003-organization-service.md)

## Context

The add-in's purpose is pushing Excel rows into Dataverse as fast as the environment
allows. The obvious home for that logic was `DataverseAddIn.Connections`, which already owns
`ServiceClient`.

Two facts shaped the decision:

- Microsoft's [bulk operations guidance][opt] states that batch APIs apply operations
  "sequentially on the server, so there's no improved efficiency per operation". Throughput
  comes from `CreateMultiple`/`UpdateMultiple`/`UpsertMultiple` plus parallelism at the
  environment's recommended degree, not from `ExecuteMultiple`.
- The platform question is still open (VSTO vs an Office Web Add-in with a backend service).
  Whichever wins, the ingestion logic is identical and moves between hosts.

## Decision

A separate `DataverseAddIn.Ingestion` assembly that references **only**
`Microsoft.CrmSdk.CoreAssemblies` — no `ServiceClient`, no MSAL, no auth. The engine takes
a `Func<IOrganizationService>` factory and an integer degree of parallelism.

`DataverseAddIn.Connections` supplies the adapter (`ServiceClient.Clone()`,
`RecommendedDegreesOfParallelism`, `EnableAffinityCookie = false`). Dependencies point
*into* the engine, never out of it.

## Rationale

- **Testability was the deciding factor, and it is now demonstrated.** The engine is
  algorithmic: chunking, strategy selection, error-index mapping, failure isolation, retry.
  Seven unit tests run against a fake `IOrganizationService` with no network and no
  credentials, in ~180 ms. Coupled to `ServiceClient` — which requires a live connection to
  construct — none of that is reachable.
- **The host is undecided.** An Azure Function behind an Office Web Add-in needs the engine
  but not the connection manager, discovery, or WinForms. Keeping the dependency one-way
  means that move costs a project reference, not a refactor.
- **Dependency hygiene, consistent with [0001][d1] and [0003][d3].** Retry and batching
  concerns stay out of assemblies that only want to connect. In a VSTO add-in sharing
  Excel's AppDomain, every assembly not loaded is a version conflict avoided.
- **Strategy is probed, not configured.** Bulk messages are unavailable on some standard
  tables, including Account and Contact. The engine queries `sdkmessagefilters` and falls
  back to `ExecuteMultiple`, so callers do not have to know which tables qualify.
- **Failure isolation earns its complexity.** On standard tables a single bad record rolls
  the whole bulk request back, and the fault says nothing about which record was at fault.
  The engine re-sends a failed chunk one record at a time, converting an opaque rollback
  into a precise per-row error list — which is what a spreadsheet user needs.

## Consequences

- Four libraries now (`Discovery`, `Connection`, `Ingestion`, `Ui`). This is the limit;
  retry, chunking and strategy stay *inside* `Ingestion` rather than splitting further.
- `Ingest` blocks, because .NET Framework has no `Parallel.ForEachAsync`. Hosts must call it
  from a background thread — mandatory in VSTO, where blocking the UI thread deadlocks Excel.
- Disabling the affinity cookie is correct for bulk work but wrong for interactive cached
  reads, so it is applied in the ingestion adapter rather than at connection time.
- `IngestionResult.ThrottledRetries` is surfaced deliberately: per Microsoft, "if you aren't
  getting some service protection limit errors, you haven't maximized the capability of your
  application". Zero retries at volume is a signal to push harder, not a success metric.

[opt]: https://learn.microsoft.com/en-us/power-apps/developer/data-platform/optimize-performance-create-update
[d1]: 0001-discovery-approach.md
[d3]: 0003-organization-service.md

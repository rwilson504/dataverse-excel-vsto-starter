# 0005 — Ship as a VSTO add-in; no Office Web Add-in, no backend service

- Status: accepted
- Date: 2026-08-18
- Supersedes the direction implied in [0004](0004-ingestion-engine.md)'s context section

## Context

Three shapes were considered for the Excel client:

1. VSTO add-in (Windows-only, in-process C#).
2. Office Web Add-in calling Dataverse directly from the task pane.
3. Office Web Add-in with a C# backend doing the ingestion.

Office Web Add-ins are Microsoft's current platform; COM/VSTO are described as "earlier
Office integration solutions that run only in Office on Windows". VSTO is **not** deprecated
for Excel — it is unsupported only in the new Outlook on Windows.

Option 3 was initially recommended on throughput grounds: a browser cannot disable the Azure
affinity cookie, and [service protection limits apply per server][spr].

Then the actual volume arrived: **20,000 rows worst case per load.**

## Decision

Build the VSTO add-in. No web add-in, no backend service.

## Rationale

- **The throughput argument evaporates at this volume.** 20,000 rows is ~100 `CreateMultiple`
  requests at 200 per request, or a few thousand small `ExecuteMultiple` requests. Against
  documented service protection limits of 6,000 requests and 20 minutes of execution time per
  user, per server, per five-minute window, that is roughly 2% of the request budget. The
  affinity cookie only matters *because* limits are per server, so its advantage is
  irrelevant here. Scale-out compute and `ServicePointManager` tuning solve problems this
  workload does not have.
- **Option 3 ends up with the costs of both and the benefits of neither.** It needs web
  hosting *and* an API, for a throughput benefit that does not materialize at 20k.
- **Hosting is the real cost, and it is unwanted.** Any Office Web Add-in requires the
  HTML/JS to be served over HTTPS from somewhere. In a GCC High context that hosting, and
  anything that touches customer data, lands inside the compliance boundary. VSTO deploys to
  the workstation and adds nothing to that boundary.
- **It reuses everything already built.** `DataverseAddIn.Discovery`, `DataverseAddIn.Connections`,
  `DataverseAddIn.Ingestion` and `DataverseAddIn.WinForms` are all net462 and load in-process. The web route
  would require reimplementing chunking, degree of parallelism, `Retry-After` handling, the
  bulk-message probe and failure isolation in TypeScript, and would lose the unit tests.

## Consequences

- Windows-only. Excel on Mac, iPad and the web are out of scope. Accepted deliberately.
- The **Office/SharePoint development workload** must be installed in Visual Studio.
  *(Corrected 2026-08-19: the workload is available in VS 2019 and 2022 on x64. It was absent
  here because this is a Windows-on-ARM machine, where VS 2022 reports it unavailable and only
  the emulated VS 2019 can install it. The original note read as a VS 2022 limitation, which
  it is not.)*
- The engine is reusable if this is revisited: it depends only on `Microsoft.Xrm.Sdk` and
  would move to a backend unchanged. That optionality is the payoff from [0004](0004-ingestion-engine.md).
- Revisit if volumes grow roughly tenfold, if unattended or scheduled loads are needed, or if
  non-Windows Excel becomes a requirement.

## Amendment to 0004 — batch size is per strategy

Field measurement showed roughly 3 records per `ExecuteMultiple` with high parallelism beats
large batches. That is consistent with the documented behaviour: `ExecuteMultiple` applies its
operations *sequentially on the server*, so a large batch is a long serial run occupying one
server thread and starving parallelism. The batch exists only to amortize per-request
overhead.

The opposite holds for `CreateMultiple`, which processes the set as one optimized unit and
gets *more* efficient as the set grows.

A single `BatchSize` was therefore wrong. It is now split:

| Option | Default | Why |
| --- | --- | --- |
| `BulkMessageBatchSize` | 200 | Larger is better; bounded by message size and time limits |
| `ExecuteMultipleBatchSize` | 3 | Small deliberately; parallelism does the work |

Both are tunable per environment.

[spr]: https://learn.microsoft.com/en-us/power-apps/developer/data-platform/send-parallel-requests

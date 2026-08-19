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

### How we got here

The recommendation changed twice, and the reversals are the useful part:

1. **Start: option 2 looks obvious.** Web add-ins are the modern platform, cross-platform,
   and centrally deployable. If you are starting a new Office project in 2026, this is the
   default answer and it is usually right.
2. **Then throughput pushed toward option 3.** Dataverse [service protection limits apply
   *per web server*][spr], so spreading requests across servers raises the effective ceiling —
   which is what disabling the Azure affinity cookie does. A browser client **always** has
   server affinity on and cannot turn it off, so a task-pane client is structurally capped
   against a .NET one. That argued for moving ingestion to a C# backend.
3. **Then the actual volume arrived: 20,000 rows worst case per load** — and the argument
   that had driven the whole design collapsed.

I recommended option 3 before knowing the number. That was the mistake: the throughput
reasoning was correct in the abstract and irrelevant at this scale.

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

## What we gained

- **No hosting, and no new compliance surface.** Nothing to stand up, secure, patch or
  certify. In GCC High that is the difference between a desktop deployment and a project.
- **Stronger authentication options.** An in-process .NET client gets the system browser
  (so FIDO keys, Windows Hello and device-compliance Conditional Access all work), a
  DPAPI-encrypted token cache on disk, and the WAM broker as an upgrade path. A task pane is
  confined to what the browser and Office dialog APIs allow.
- **No structural throughput ceiling.** Affinity cookie off, degree of parallelism set from
  the environment's own hint. Headroom we do not need today but cannot get from a browser at
  any price.
- **The ingestion logic is ordinary testable C#.** 173 offline unit tests exist because the
  engine takes an `IOrganizationService` and nothing else. The TypeScript equivalent would
  have been reimplemented and largely untested.
- **Direct access to the workbook object model** in-process, rather than across the Office.js
  async boundary.

## What we gave up

Worth being honest about, because these are the reasons most projects should choose the
other way:

- **Windows-only.** Excel on Mac, iPad and the web are out of scope entirely. This is the
  big one, and it is irreversible without a rewrite of the host layer.
- **Central deployment.** Web add-ins deploy from the Microsoft 365 admin centre to users or
  groups, and update by replacing files on a server. VSTO needs a per-machine install —
  ClickOnce, MSI or Intune — plus a code-signing certificate to avoid "Unknown Publisher".
  Rolling out a fix is a deployment, not a redeploy.
- **The modern platform.** VSTO is supported for Excel but is explicitly the older model, and
  COM/VSTO is already unsupported in the new Outlook on Windows. That is not a deprecation
  notice for Excel, but it is a direction of travel, and a project with a long horizon should
  weigh it.
- **A smaller pool of developers and examples.** New Office samples target Office.js.
- **Machine-level friction for contributors** — the Office/SharePoint workload, a signing
  key, and Visual Studio. The libraries build with the plain .NET SDK, but the add-in does
  not.

The Windows-only and central-deployment costs are real and were accepted knowingly, against
a user population that is Windows desktop Excel today.

## Consequences

- The **Office/SharePoint development workload** must be installed in Visual Studio.
  *(Corrected 2026-08-19: the workload is available in VS 2019 and 2022 on x64. It was absent
  here because this is a Windows-on-ARM machine, where VS 2022 reports it unavailable and only
  the emulated VS 2019 can install it. The original note read as a VS 2022 limitation, which
  it is not.)*
- The engine is reusable if this is revisited: it depends only on `Microsoft.Xrm.Sdk` and
  would move to a backend unchanged. That optionality is the payoff from [0004](0004-ingestion-engine.md)
  and it is deliberately the hedge against this decision being wrong.

## What would change this decision

Any one of these, on its own:

| Trigger | Why it flips |
| --- | --- |
| Volumes grow ~10x | The per-server throughput ceiling starts to bind, and the affinity-cookie advantage becomes real |
| Excel on Mac, iPad or web becomes a requirement | VSTO cannot serve it at all |
| Unattended or scheduled loads are needed | No interactive user means no desktop host; this becomes a service |
| Central deployment becomes a hard requirement | Per-machine installs stop scaling organisationally |
| Microsoft signals deprecation for Excel specifically | The direction of travel becomes a date |

The migration cost is deliberately bounded: `DataverseAddIn.Ingestion` and
`DataverseAddIn.Discovery` move to a backend as-is, and only the host and UI layers are
rewritten.

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

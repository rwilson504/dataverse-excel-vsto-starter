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
   against a .NET one. That argued for moving ingestion to a C# backend.   *(Overstated — corrected 2026-08-20, see the amendment below. The affinity cookie is the
   only browser-specific limit; `$batch` and `CreateMultiple` are Web API features too.)*3. **Then the actual volume arrived: 20,000 rows worst case per load** — and the argument
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
  *(Half right — corrected 2026-08-20, see the amendment below. The hosting requirement is
  real and intrinsic, but no customer data passes through that host in a direct
  task-pane-to-Dataverse design.)*
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

## Amendment 2026-08-20 — the decision holds, three of its arguments do not

Prompted by a challenge to the throughput reasoning, the claims in this record were checked
against Microsoft's documentation and against a published implementation of the alternative.
**The decision does not change. Most of the case for it does.**

### Confirmed

- **`ExecuteMultiple` gains no server-side efficiency.** [Optimize performance for bulk
  operations][opt] states it verbatim: "Each operation within the request is applied
  sequentially on the server, so there's no improved efficiency per operation." The strongest
  claim in 0004 is quotable, not inferred.
- **Service protection limits** are 6,000 requests and 20 minutes of execution per user in a
  five-minute sliding window, with a default concurrency ceiling of 52.
- **Browsers cannot disable server affinity**, and limits apply per server. Both verbatim in
  [Send parallel requests][par].
- **Bulk messages want 100–1,000 records** per request for standard tables, 100 for elastic.
- **A hosted HTTPS endpoint is genuinely unavoidable** for a web add-in. The manifest's
  `SourceLocation` "must be an HTTPS address, not a file path", and framework choice is
  irrelevant — React compiles to static files that still need serving.

### Corrected

1. **The throughput argument for VSTO is gone, not merely unnecessary.** `$batch` *and*
   `CreateMultiple`/`UpdateMultiple`/`UpsertMultiple` are Web API features — the latter as
   bound actions — and a browser can issue them in parallel. The only browser-specific limit
   is the affinity cookie, which binds solely near per-server ceilings. At 20k rows it never
   does. This record framed the gap as structural; it is one narrow mechanism.
2. **The compliance-boundary framing was too strong.** In a direct task-pane-to-Dataverse
   design the host serves static assets only; no customer data transits or rests there. The
   real cost is operational — standing up, securing, patching and monitoring an endpoint, and
   possibly having code that runs against regulated data reviewed. Whether that is a project
   or a checkbox depends entirely on what the tenant already has approved.
3. **`ExecuteMultiple` and `$batch` are not equivalent**, which this record and 0004 both
   assumed because Microsoft's guidance pairs them. `ExecuteMultiple` runs each operation in
   its own transaction with `ContinueOnError`. A `$batch` **changeset is atomic**, and supports
   `Content-ID` referencing — create a parent as `$1`, bind a child to `"$1"` in the same set.
   That is a real capability the SDK message lacks, and it matters for related rows across
   sheets. The choice is about transaction shape and error granularity, not speed.

### Strengthened

- **Authentication really is simpler in-process.** Listed under "What we gained" as an
  assertion; now evidenced. Identity providers refuse to render sign-in pages in an iframe, so
  a task pane must route through the Office Dialog API — a separate browser instance with its
  own runtime and no shared storage — and return the token through `messageParent`, which
  carries only strings or booleans. In practice that is separate login/logout/post-logout
  pages, extra bundler entries, and state plumbing. Those pages must also be served from the
  same domain as the task pane, so it compounds with the hosting requirement.

### Under-weighted

**Ongoing friction.** "Machine-level friction for contributors" understates it. Building and
debugging this add-in has cost: the Office/SharePoint workload unavailable on Windows on ARM
outside an emulated VS 2019, a code-signing certificate, a build script whose job is partly to
keep the .NET CLI away from the VSTO project, per-machine registration, and WinForms-specific
defects that do not exist in a browser. A web add-in has none of it.

### Where this leaves the decision

Unchanged, but resting on **hosting avoidance and sign-in simplicity** rather than throughput,
compliance surface, and platform capability. Both surviving arguments are real and both are
environment-dependent.

Stated plainly: **in a tenant with approved static hosting, the web add-in is the better
choice.** This one holds because of where it runs, not because it is the better platform.

### Prior art

The alternative is not hypothetical. Tae Rim Han has published both halves of it:

- [Let's Bring Dataverse to Excel Using Office Add-ins][tae-addin] — a React/TypeScript task
  pane calling Dataverse directly, no backend, including the Dialog API sign-in flow.
- [Execute Web API Batch Operations Without ExecuteMultiple][tae-batch] — composing `$batch`
  bodies and changesets by hand, with the `Content-ID` referencing examples.

Note that the first does row-by-row CRUD rather than bulk operations, so the browser-side
throughput path remains untested by either of us.

[opt]: https://learn.microsoft.com/power-apps/developer/data-platform/optimize-performance-create-update
[par]: https://learn.microsoft.com/power-apps/developer/data-platform/send-parallel-requests
[tae-addin]: https://taerimhan.com/lets-bring-dataverse-to-excel-using-office-add-ins/
[tae-batch]: https://taerimhan.com/execute-web-api-batch-operations-without-executemultiple/

The opposite holds for `CreateMultiple`, which processes the set as one optimized unit and
gets *more* efficient as the set grows.

A single `BatchSize` was therefore wrong. It is now split:

| Option | Default | Why |
| --- | --- | --- |
| `BulkMessageBatchSize` | 200 | Larger is better; bounded by message size and time limits |
| `ExecuteMultipleBatchSize` | 3 | Small deliberately; parallelism does the work |

Both are tunable per environment.

[spr]: https://learn.microsoft.com/en-us/power-apps/developer/data-platform/send-parallel-requests

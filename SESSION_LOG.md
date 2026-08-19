# Session log — DataverseDiscovery

## 2026-08-18

- Agent: GitHub Copilot (default)
- Prompts: 1
- Summary: Researched the options for listing a user's Dataverse environments from
  .NET Framework 4.6.2 ahead of an Excel VSTO add-in. Scaffolded `DataverseDiscovery`
  with a net462 class library (`DataverseAddIn.Discovery`) and a console harness
  (`DataverseAddIn.Samples.ConsoleHost`). Implemented MSAL.NET public-client auth with a
  DPAPI-backed token cache, a Global Discovery Service OData client, and a minimal
  `WhoAmI` caller to prove the per-environment token. Recorded the
  REST-over-`ServiceClient` choice in `decisions/0001-discovery-approach.md`.
  Solution builds clean.
- Open: needs a real Entra app registration client ID in
  `src/DataverseAddIn.Samples.ConsoleHost/App.config` before it can be run end to end.

- Prompts: 2
- Summary: Added GCC and GCC High support. Confirmed from Microsoft docs that GCC uses
  public Microsoft Entra ID while GCC High and DoD require Microsoft Entra Government,
  so the two need separate app registrations and separate sign-ins. Added
  `MultiCloudDiscoveryClient` (sequential fan-out, per-cloud failure collection),
  stamped `DataverseInstance.Cloud`, added `UsesGovernmentIdentity`,
  `SharesIdentityWith` and `TryGetCloudFromUrl`, and partitioned the MSAL token cache
  per authority host and client ID — previously commercial and government authenticators
  would have overwritten each other's cache. Console sample now takes a `Clouds` list
  with `Key.<Cloud>` config overrides. Recorded in
  `decisions/0002-multi-cloud-model.md`. Builds clean.
- Open: needs a second app registration in Entra Government (`portal.azure.us`) before
  the GCC High path can be exercised.

- Prompts: 3
- Summary: Answered whether Microsoft's sample app registration
  (`51f81489-12ee-4a9e-aaae-a2591f45987d`, `http://localhost`,
  `AadAuthorityAudience.AzureAdMultipleOrgs`) can serve all three clouds. It is fine for
  Commercial and should work for GCC (shared public Entra identity), but the OAuth doc
  requires your own registration for published apps, and it should not be relied on for
  GCC High. Recorded it as a labelled dev-only value in App.config and added a
  "Do I need my own app registration?" section to the README.
- Note: attempted to verify GCC High availability by probing
  `GET /authorize` on `login.microsoftonline.us`. **The technique does not work** — Entra
  accepted a deliberately bogus `client_id` against a real tenant without error, so app
  resolution is deferred until after credentials. Do not use unauthenticated authorize
  requests as an app-existence check.

- Prompts: 4
- Summary: User supplied a real GCC High org URL. Confirmed
  `TryGetCloudFromUrl` resolves it to `UsGovernmentHigh` with the correct discovery
  endpoint and `login.microsoftonline.us` authority, matching the org's own
  `WWW-Authenticate` challenge. **Corrected the previous turn's guidance**: the sample
  client ID `51f81489-12ee-4a9e-aaae-a2591f45987d` *does* resolve in Microsoft Entra
  Government — the device-code endpoint issued a code for it against the real GCC High
  tenant, while a bogus client ID returned `AADSTS700016` as a control. Updated README
  and App.config accordingly.
- Techniques worth keeping:
  - Unauthenticated `GET <org>/api/data/v9.2/WhoAmI` returns a `WWW-Authenticate` header
    carrying `authorization_uri` (authority + tenant ID) and `resource_id` (exact token
    audience). Fastest way to learn what an unknown environment expects.
  - `POST <authority>/<tenant>/oauth2/v2.0/devicecode` validates `client_id` eagerly and
    is a valid app-existence probe. `GET /authorize` is not.

- Prompts: 5
- Summary: User pushed back on the claim that the sample client ID would be blocked by
  Conditional Access in GCC High tenants. Correct — it is the ID used by XrmToolBox and
  much of the Dataverse community tooling, so blocking it would break admin tooling.
  Conditional access there is far more likely to be MFA, device compliance, or a
  named-location rule permitting corporate network / AVD. Replaced the speculation in
  README and App.config with an `AADSTS` error-code table and the observation that
  **consent policy** (`AADSTS65001`), not app blocking, is the usual blocker in locked-down
  tenants. Also documented that the device-code probe proves the app resolves but does not
  prove a consented service principal exists.
- Lesson: two speculative assertions in consecutive turns (GCC High app resolution, then
  CA blocking), both wrong. Anything about *tenant policy* is unverifiable from here —
  state it as a question or an error code to look for, never as a prediction.

- Prompts: 6
- Summary: **End-to-end verified against a live GCC High tenant.** Configured the console
  harness with `Clouds=UsGovernmentHigh` and the sample client ID, ran it interactively.
  Results:
  - Interactive sign-in through the system browser succeeded — no Conditional Access
    block, confirming the user's expectation from the previous turn.
  - Global Discovery returned 14 environments from
    `globaldisco.crm.microsoftdynamics.us`.
  - `WhoAmI` succeeded against a selected environment, proving the per-environment token.
  - Second run required **no sign-in** (DPAPI cache hit), and a *different* environment
    resolved silently, proving per-resource silent acquisition from one refresh token.
  - Cache file written as
    `%LOCALAPPDATA%\DataverseDiscovery\login.microsoftonline.us.<clientId>.msalcache`,
    confirming the authority/client partitioning.
- Fixed: row-index column in `PrintInstances` misaligned at 10+ rows
  (`{i + 1,-1}` does not pad); now padded to the width of the largest index.
- Note: App.config currently holds the sample client ID and is pinned to
  `UsGovernmentHigh` for testing. Reset `Clouds` and supply a real registration before
  using this as a template for the add-in.

- Prompts: 7
- Summary: `dotnet build` failed for the user with "need a project file" — they were in
  `D:\temp`, one level above the solution. Two follow-on findings:
  - The .NET 10 SDK's `dotnet new sln` defaults to **`.slnx`**, so the original solution
    was `DataverseDiscovery.slnx`. Bare `dotnet build` found it, but explicit `.sln` paths
    failed with `MSB1009`. Regenerated with `--format sln` for Visual Studio / VSTO
    compatibility and deleted the `.slnx`.
  - The rebuild then failed `MSB3026: file is locked by "PowerShell 7 (33360)"` — PID
    33360 was the agent's own persistent shell, locked by the earlier `Add-Type -Path`
    verification. Assemblies loaded that way can never be unloaded; exiting the shell was
    the only fix. Use `[Reflection.Assembly]::Load([IO.File]::ReadAllBytes(...))` instead.
  - README run instructions now state the working directory explicitly and note the
    `.slnx` default.

- Prompts: 8
- Summary: User confirmed they will use the C# SDK, so added the Organization Service path.
  New `DataverseAddIn.Connections` project (net462) wrapping
  `Microsoft.PowerPlatform.Dataverse.Client` 1.2.26, using the caller-managed-auth
  constructor `ServiceClient(Uri, Func<string,Task<string>>, bool, ILogger)` fed by the
  existing `DataverseAuthenticator`. Kept separate from `DataverseAddIn.Discovery` so the SDK
  graph only loads when needed. Added `DataverseEnvironmentReference` for the
  "user pastes a URL" path, and a command-line argument to the console sample that skips
  discovery entirely. Recorded in `decisions/0003-organization-service.md`.
- Verified live against GCC High, both entry points:
  - discovery selection → Web API WhoAmI **and** `ServiceClient.Execute(WhoAmIRequest)`,
    `ConnectedOrgFriendlyName = RAW`;
  - bare host `contoso.crm.microsoftdynamics.us` (no scheme, no discovery call) →
    normalized, cloud inferred as `UsGovernmentHigh`, both callers succeeded silently.
  - App host and `.api.` host both work as environment URL and token audience.
- Fixed: `WhoAmIResponse` collided with `Microsoft.Crm.Sdk.Messages.WhoAmIResponse`
  (`CS0104`); renamed the Web API DTO to `WhoAmIResult`.
- Gotcha: the console kept reading a stale buffered `6` from stdin across runs, so the
  interactive URL prompt could not be tested that way. Adding a command-line argument was
  both the fix and a better feature.

- Prompts: 9
- Summary: Built the connection-manager UI. Added `ConnectionProfile`, `ConnectionStore`
  (JSON under `%APPDATA%\DataverseDiscovery\connections.json`, no secrets) and
  `DataverseConnectionManager` (per-cloud authenticator cache, active `ServiceClient`,
  `ConnectionChanged` event) to `DataverseAddIn.Connections`. New `DataverseAddIn.WinForms` WinForms library
  with `ConnectionManagerForm`, `AddConnectionForm` (live cloud detection from the typed
  host) and `DiscoveryPickerForm`. New `DataverseAddIn.Samples.WinFormsHost` exe mirroring the ribbon's two
  commands. VSTO source files (`ThisAddIn.cs`, `DataverseRibbon.cs`,
  `DataverseRibbon.xml`) in `src/Excel.AddIn`. Solution builds clean.
- **Blocker for the VSTO project**: Visual Studio 2022 Enterprise is installed and Excel is
  present, but the **Office/SharePoint development workload is not** — verified, there is no
  `...\MSBuild\Microsoft\VisualStudio\v17.0\OfficeTools\` folder. The .NET SDK cannot build
  VSTO projects, so `src/Excel.AddIn` ships as source files plus setup steps in the README
  rather than a `.csproj` that would not load.
- Ribbon enable/disable uses a `getEnabled` callback plus
  `IRibbonUI.InvalidateControl("btnWhoAmI")` driven by `ConnectionChanged`, rather than
  cached state.

- Prompts: 10-11
- Summary: User raised Office Web Add-ins as the modern platform and asked whether
  parallelism survives the move. Researched both. Key findings:
  - Office Add-ins are the current platform (cross-platform, central deployment); COM/VSTO
    are "earlier ... run only in Office on Windows" but **not deprecated** for Excel.
  - **`ExecuteMultiple` is not a throughput tool** — operations are "applied sequentially
    on the server, so there's no improved efficiency per operation". Throughput comes from
    `CreateMultiple`/`UpdateMultiple`/`UpsertMultiple` plus parallelism at `x-ms-dop-hint`.
  - A browser/taskpane **cannot disable the affinity cookie**, and service protection limits
    apply per server — a structural throughput cap on the pure web add-in route.
- Built `DataverseAddIn.Ingestion` as its own assembly depending only on
  `Microsoft.CrmSdk.CoreAssemblies`: strategy probing via `sdkmessagefilters`, chunking,
  `Parallel.ForEach` at the recommended DOP, service protection retry honouring
  `Retry-After`, and per-record failure isolation after an all-or-nothing bulk rollback.
  Adapter in `DataverseAddIn.Connections` supplies `Clone()` + DOP + affinity off.
  Recorded in `decisions/0004-ingestion-engine.md`.
- Added `tests/DataverseAddIn.Ingestion.Tests` (xunit, net462): **7 tests pass offline in ~180 ms**
  against a fake `IOrganizationService`, proving the testability argument for the split.
- Gotcha (second occurrence this session): the running `DataverseAddIn.Samples.WinFormsHost` held the output DLLs
  and broke the build with `MSB3027 ... locked by "DataverseAddIn.Samples.WinFormsHost"`. Same class as the earlier
  `Add-Type` lock — stop launched processes before rebuilding.

- Prompts: 12-13
- Summary: **Platform decided — VSTO, Windows-only, no web add-in and no backend service.**
  Recorded in `decisions/0005-platform-choice.md`. The deciding input was the real volume:
  20,000 rows worst case, which is ~2% of the documented service protection request budget.
  At that scale the browser's inability to disable the affinity cookie is irrelevant, so the
  backend carried the cost of both web hosting and an API for no throughput gain. Hosting
  also pulls into the GCC High compliance boundary, which the user wants to avoid.
  I had previously recommended the backend; revised once the number was known.
- **Engine fix from the user's field measurement**: ~3 records per `ExecuteMultiple` with
  high parallelism beats large batches, because ExecuteMultiple applies operations
  sequentially on the server — a large batch is a long serial run on one server thread that
  starves parallelism. The opposite is true for `CreateMultiple`, which is optimized as a
  single set. A single `BatchSize` was therefore wrong; split into
  `BulkMessageBatchSize` (200) and `ExecuteMultipleBatchSize` (3), with a test pinning the
  small default. 8/8 tests pass.
- Open: install the **Office/SharePoint development** workload in Visual Studio, then create
  the VSTO project and drop in `src/Excel.AddIn`. Also still untested against a real
  environment — the fake proves the engine logic, not the wire.

- Prompts: 14-16
- Summary: VSTO tooling turned into a toolchain problem.
  - I claimed the Office workload was available in VS 2022 because
    `Microsoft.VisualStudio.Workload.Office` appears in the 17.14.37 installer catalog.
    **Wrong** — the installer reports it as unavailable. Catalog presence is not the same as
    installable availability; that check was inconclusive, not proof.
  - Both VS 2022 and VS 2019 Enterprise are installed; the workload is only installable in
    VS 2019 here.
  - Tried pinning `global.json` to .NET SDK 5.0.416 so VS 2019's MSBuild 16.11 could load the
    SDK-style projects. Failed: **the machine is ARM64 and .NET 5 has no `win-arm64` SDK**.
    Reverted.
  - Resolution: VS 2019 never builds the libraries. `dotnet build` (ARM64-native, SDK 10)
    produces them; the VSTO project takes **file references** to the prebuilt DLLs and is
    built by .NET Framework MSBuild only, so no .NET SDK is involved.
  - Enabled `CopyLocalLockFileAssemblies` so each library's `bin` contains its full
    dependency closure, making that reference step practical.
- Measured dependency closure: `DataverseAddIn.Discovery` 8 DLLs, `DataverseAddIn.Ingestion` 14,
  `DataverseAddIn.Connections` 135, `DataverseAddIn.WinForms` 136. `ServiceClient` alone brings 120+
  assemblies — concrete vindication of decisions 0001/0003/0004, and a reason to prefer
  `DataverseWebApiClient` when SDK message types are not needed.
- Environment facts worth remembering: OS ARM64, .NET SDK 10.0.303 win-arm64, **Office x64**
  (so the add-in runs emulated; keep `PlatformTarget` AnyCPU).

- Prompts: 17
- Summary: Office/SharePoint workload confirmed installed in **VS 2019 Enterprise**, with
  `...\v16.0\OfficeTools\Microsoft.VisualStudio.Tools.Office.targets` present. Asked the user
  to create the VSTO project shell in VS rather than hand-authoring the csproj, since the
  designer plumbing and temporary signing certificate are generated and a hand-rolled project
  tends to break the designer.
- Built `SheetMapper` in `DataverseAddIn.Ingestion`: converts an `object[,]` block — exactly what
  Excel's `Range.Value2` returns — into `Entity` records, with per-row error reporting keyed
  to the worksheet row number. No Office dependency, so it is unit testable.
  - Handles the **1-based array** Excel returns by reading `GetLowerBound`, not assuming 0.
  - Coerces the types Excel actually hands back: all numbers arrive as `double`, dates as OLE
    Automation serials via `DateTime.FromOADate`, plus Money/OptionSet/EntityReference/Guid
    and the yes/no spellings users type.
- Bug found by its own test: a **wholly blank row raised a "required" error for every
  required column**. Trailing blank rows are ubiquitous in Excel selections, so this would
  have flooded the error list. Fixed by skipping blank rows before validation.
- 18/18 tests pass.

- Prompts: 18
- Summary: **The VSTO add-in now builds.** User created `src\DataverseAddIn.Excel` in VS 2019 and it
  failed to compile. Fixes:
  - **Namespace case.** The generated `ThisAddIn.Designer.cs` uses `RootNamespace`
    `DataverseAddIn.Excel`; my files declared `Excel.AddIn`. C# namespaces are case-sensitive, so the
    partial class never merged and the only symptom was
    `CS0115: 'ThisAddIn.CreateRibbonExtensibilityObject()': no suitable method found to
    override`. Note the folder names collided on case-insensitive Windows, which is how my
    files landed in the generated project in the first place.
  - `DataverseRibbon.xml` was not in the project — added as `EmbeddedResource`, and the
    `GetResourceText` argument corrected to `DataverseAddIn.Excel.DataverseRibbon.xml`.
  - Added a `System.Configuration` reference and `app.config` (deploys as
    `DataverseAddIn.Excel.dll.config`).
  - Clean rebuild: 138 DLLs, `DataverseAddIn.Excel.vsto`, `.dll.manifest`, `.dll.config` all produced.
- **Corrected an earlier wrong conclusion.** I had planned around "VS 2019 cannot build the
  SDK-style libraries, so use file references". Wrong — VS 2019's MSBuild builds them fine
  because it uses the .NET SDK bundled with Visual Studio, not the .NET 10 CLI SDK. The user
  had already added ordinary `ProjectReference` entries and they work. I removed my redundant
  file references and copy target; README rewritten. The failed `global.json` experiment was
  about the *CLI* SDK resolver, a different mechanism.
- Open: run it in Excel (F5) and confirm the ribbon loads and MSAL sign-in parents to Excel.
  VSTO does not honour binding redirects from `.dll.config` — with a 138-assembly closure,
  an `AppDomain.AssemblyResolve` handler may be needed if load failures appear.

- Prompts: 19
- Summary: First F5 threw `NullReferenceException` in `DataverseRibbon.OnLoad` —
  `ThisAddIn.Connections` was null. **VSTO lifecycle ordering:** Office creates the ribbon via
  `CreateRibbonExtensibilityObject` and raises its `OnLoad` *before* `ThisAddIn_Startup`, so
  anything the ribbon touches cannot be initialised in Startup.
  Fixed by making `Connections` a lazily-created static (double-checked under a lock) instead
  of assigning it in Startup; `Shutdown` still disposes it. `GetWhoAmIEnabled` no longer needs
  its null guard. Builds clean.

- Prompts: 20
- Summary: Added per-connection **name and colour**.
  - `ConnectionProfile` gains `Color` (hex string, so the model needs no System.Drawing
    reference) and `NameIsAuto`. `SuggestName` derives a placeholder from the first host
    label; on the first successful connect the manager replaces an auto name with
    `ServiceClient.ConnectedOrgFriendlyName` and saves.
  - Replaced `AddConnectionForm` with a single `ConnectionDetailsForm` used by all three
    paths — add by URL, add from discovery, and a new **Edit...** button. It carries a
    10-colour palette plus a custom picker, and shows the detected cloud live.
  - `ConnectionManagerForm` list is now owner-drawn with a colour chip per row.
  - Used `TextBox.Modified` to tell user edits from programmatic ones, so a typed name is
    never clobbered by a later URL change.
- Build fights worth recording:
  - Adding `DataverseAddIn.Excel` to `DataverseDiscovery.sln` breaks `dotnet build` — the CLI has no
    OfficeTools targets. The VSTO project now lives in its own `src/DataverseAddIn.Excel/DataverseAddIn.Excel.sln`
    (hand-written; `dotnet sln add` also fails because it evaluates the project).
  - After the CLI had touched it, the VSTO project failed with *"doesn't list 'win' as a
    RuntimeIdentifier"*. Root cause was the **`project.assets.json` the .NET 10 SDK wrote into
    `src/DataverseAddIn.Excel/obj`**; VS 2019's legacy NuGet targets choke on it. Deleting `obj` fixed
    it. Adding `RuntimeIdentifiers` — the error's own suggestion — did not.
  - NuGet references of SDK-style projects do **not** flow to a legacy project at compile
    time, so `Microsoft.Xrm.Sdk`, `Microsoft.Crm.Sdk.Proxy` and
    `Microsoft.PowerPlatform.Dataverse.Client` need explicit `HintPath` references.
  - `ProjectReference` alone copied only 37 DLLs; ServiceClient needs its ~130-assembly
    closure, so the copy target from the `DataverseAddIn.WinForms` output is load-bearing. Output is
    back to 138 DLLs.

- Prompts: 21
- Summary: Discovery picker improvements.
  - Added a **search box** filtering on name, URL, unique name, region and version, with a
    "x of y environment(s)" count. Disabled until a load succeeds.
  - Fixed the **clipped "Cloud" label** (fixed 40px width showed "Clo") by switching to
    `AutoSize`, and widened the combo.
  - Cloud values are now human-readable via a new `DataverseCloud.GetDisplayName()` in
    `DataverseAddIn.Discovery` — "US Government Community Cloud (GCC)", "US Government High
    (GCC High)", etc. The combo holds a small `CloudItem` wrapper so the enum value survives.
- **Operational rule discovered**: `DataverseAddIn.Excel` is back in `DataverseDiscovery.sln`, and
  VS 2019's MSBuild builds the whole solution fine. But running `dotnet build` on that
  solution both fails *and* writes `obj/project.assets.json` into the VSTO project, which
  then breaks the next VS build with the bogus "doesn't list 'win' as a RuntimeIdentifier"
  error. Delete `src/DataverseAddIn.Excel/obj` to recover. Use VS 2019 MSBuild for the solution;
  `dotnet test` on the test project directly is safe. Removed the redundant second solution.
- 18/18 tests still pass.

- Prompts: 22
- Summary: Connection manager list converted from an owner-drawn `ListBox` to a
  `ListView` in Details view, giving real column headers: **Name / Cloud / Environment URL**.
  Cloud now shows `GetDisplayName()` rather than the raw enum. The colour moved from a
  hand-painted chip to an `ImageList` of generated 14x14 swatch bitmaps set as each row's
  icon, so the grid renders natively instead of being fully owner-drawn. The active
  connection's row is bold. `Reload()` now also runs after a successful connect, so the
  auto-name-to-friendly-name swap and the bold marker both show immediately. Swatch bitmaps
  are disposed with the form.

- Prompts: 23-24
- Summary: Fixed WinForms scaling, then added icons.
  - **Root cause of clipped text: the display is at 200% scaling** (`AppliedDPI 192`) and every
    form was hand-coded with absolute `SetBounds` pixels. Critically, WinForms only applies
    font scaling when `AutoScaleDimensions` is set — the designer emits it, hand-built forms
    do not, so `AutoScaleMode.Font` alone does nothing.
  - Added `FormScaling` helper: sets `SystemFonts.MessageBoxFont`, `AutoScaleDimensions
    (7,15)` and `AutoScaleMode.Font`, plus factory methods for table/flow layouts, labels and
    buttons. Rewrote all three dialogs and the test host with `TableLayoutPanel` + `Dock` +
    `AutoSize` instead of pixel maths. `ConnectionDetailsForm` is now `AutoSize`.
  - Icons use **Segoe Fluent Icons** with **Segoe MDL2 Assets** fallback, rendered to bitmaps
    at a size derived from the control's font — vector, so no 16/24/32/48 asset set and it
    stays crisp at any scaling. `Glyphs.IsAvailable` degrades to text-only if neither font
    exists. Window icons via `Icon.FromHandle` + `Clone` + `DestroyIcon` to avoid leaking the
    HICON.
  - Verified all 18 candidate codepoints actually render in both fonts by rasterising each and
    comparing against an unassigned private-use codepoint, rather than trusting a glyph chart.
- Note: .NET Framework 4.8 is installed but projects target 4.6.2; true per-monitor DPI needs
  4.7+. Not pursued — in a VSTO add-in the process DPI awareness belongs to Excel, so font
  scaling is the reliable lever.

- Prompts: 25
- Summary: Renamed and restructured for release as a public starter repo.
  Solution `DataverseDiscovery.sln` → **`DataverseExcelAddIn.sln`**; all projects moved to the
  `DataverseAddIn.*` root and split into `src/` (libraries + the VSTO add-in), `samples/`
  (console and WinForms hosts) and `tests/`.

  | Old | New |
  | --- | --- |
  | `Dataverse.Discovery` | `DataverseAddIn.Discovery` |
  | `Dataverse.Connection` | `DataverseAddIn.Connections` |
  | `Dataverse.Ingestion` | `DataverseAddIn.Ingestion` |
  | `Dataverse.Ui` | `DataverseAddIn.WinForms` |
  | `Excel.Addin` | `DataverseAddIn.Excel` |
  | `Discovery.ConsoleSample` | `DataverseAddIn.Samples.ConsoleHost` |
  | `Ui.TestHost` | `DataverseAddIn.Samples.WinFormsHost` |

- Reasons beyond taste: a root namespace literally named `Excel` collides with the idiomatic
  `using Excel = Microsoft.Office.Interop.Excel;`; `Dataverse.*` reads as a first-party
  Microsoft namespace; and the `AddIn`/`Addin` casing had already caused a build failure.
- Deliberately **not** `DataverseAddIn.Samples.Console` — a namespace ending in `Console`
  shadows `System.Console` inside the project that calls `Console.WriteLine` throughout.
- Done with a script that moved directories, renamed project/pfx/user files, token-replaced
  across all text files (longest key first so `Dataverse.Ingestion.Tests` was not clipped),
  fixed the extra `..\` hop for the relocated samples, and regenerated the `.sln` by parsing
  the old one so project GUIDs survived.
- Also removed the stale `HKCU:\Software\Microsoft\Office\Excel\Addins\Excel.Addin`
  registration, which pointed at a `.vsto` path that no longer exists; Excel now shows only
  `DataverseAddIn.Excel`.
- Verified: solution builds, 18/18 tests pass, add-in output still 138 DLLs with `.vsto` and
  `.dll.config`. README rewritten for the new layout, including replacing the obsolete
  "create the VSTO project yourself" steps with F5 instructions.
- Remaining for release: rename the repo folder itself (still `DataverseDiscovery`) when
  creating the git repo — suggest `dataverse-excel-vsto-starter`. Add LICENSE and CI that
  builds/tests the libraries only, since hosted runners lack the Office workload.

## 2026-08-19

- Agent: GitHub Copilot (default)
- Prompts: 2
- Summary: Planned the move from OAuth-only to multiple authentication types, using
  `MscrmTools.Xrm.Connection` and the Dataverse OAuth Learn article as references, then
  implemented step 1. Extracted `IDataverseTokenSource` (`Cloud`, `IsInteractive`,
  `SupportsGlobalDiscovery`, `GetTokenAsync`, `SignOutAsync`) plus a
  `GetDiscoveryTokenAsync` extension, made `DataverseAuthenticator` implement it, and
  retyped every consumer: `GlobalDiscoveryClient`, `MultiCloudDiscoveryClient`,
  `DataverseWebApiClient`, `DataverseServiceClientFactory`, and
  `DataverseConnectionManager` (`GetAuthenticator` -> `GetTokenSource`). Behaviour is
  unchanged. Recorded in `decisions/0006-token-source-abstraction.md`. All SDK-style
  projects build clean.
- Design points captured for the remaining steps:
  - Public clients use `<resource>/user_impersonation`; **confidential clients use
    `<resource>/.default`**. `BuildScope` still hardcodes the former, so scope must move
    onto the credential.
  - `DataverseConnectionManager` caches by cloud alone, and
    `DataverseAuthOptions.ResolveTokenCacheFilePath()` keys on authority host + client ID.
    Both collide as soon as two auth types target one cloud; key on
    `(Cloud, Kind, ClientId, TenantId, principal)`.
  - Confidential clients use MSAL's `AppTokenCache`, not `UserTokenCache` —
    `DpapiTokenCache.Attach` needs an overload or secret/cert flows re-acquire every call.
  - Secrets must not go in `connections.json`. Store a reference plus a DPAPI blob (or
    Windows Credential Manager); do not copy MscrmTools' shared-passphrase `CryptoManager`,
    where a copied config file plus the open-source passphrase is a decrypt.
  - On-prem AD/IFD have no bearer token at all, so they need a separate
    `IDataverseCredential` returning a null token source, and `SupportsGlobalDiscovery`
    is what lets the UI fall back to URL entry.
- Gotcha (already in the notes, hit again): `dotnet build` on the solution restores the VSTO
  project and writes `project.assets.json` + `*.nuget.g.*` into
  `src/DataverseAddIn.Excel/obj`, which then breaks Visual Studio's legacy NuGet targets with
  "doesn't list 'win' as a RuntimeIdentifier". Deleted those four files afterwards. Build the
  SDK-style projects individually instead of the whole solution.

- Prompts: 3
- Summary: Step 2 of the authentication work. Added `DataverseAuthKind` (all nine kinds,
  persisted by name), `AuthField` flags, and `AuthKindDescriptor` to
  `DataverseAddIn.Discovery`; added `IDataverseCredential` and `InteractiveCredential` to
  `DataverseAddIn.Connections`. `DataverseConnectionManager` now caches credentials per
  cloud and `ConnectAsync` goes through `IDataverseCredential.CreateClientAsync`.
  Behaviour unchanged; all projects build, samples untouched.
- Two shape decisions worth remembering:
  - The **enum declares every kind, the descriptor registry declares only implemented
    ones.** The enum is a persisted contract so naming it once is cheaper; the descriptor
    table drives UI, so `AuthKindDescriptor.Supported` must never offer a kind that throws.
  - The credential entry point is a static `WithCredentials(...)`, not a second constructor.
    `Func<DataverseCloud, DataverseAuthOptions>` and `Func<DataverseCloud, IDataverseCredential>`
    have the same arity, so `cloud => null` would be an ambiguous call.
- `IDataverseCredential.TokenSource` is deliberately nullable — that null is the only place
  the token-less on-prem kinds are modelled, and `GetTokenSource` converts it into a
  `NotSupportedException` naming the kind.
- Gotcha: `dotnet build` takes exactly one project (`MSB1008`), so building several
  individually needs a loop, not a space-separated list.
- Next (step 3): profile fields for auth kind and identity, DPAPI/Credential Manager secret
  store, and the composite `(Cloud, Kind, ClientId, TenantId, principal)` cache key that
  both `DataverseConnectionManager` and `DataverseAuthOptions.ResolveTokenCacheFilePath()`
  need before a second kind exists.

- Prompts: 4
- Summary: Step 3 — connection identity, secret storage, and the cache key. Added
  `CredentialSpec` (`Cloud, Kind, ClientId, TenantId, Principal`, case-insensitive value
  equality) and made it the credential dictionary key in `DataverseConnectionManager`,
  replacing `DataverseCloud`. Added auth fields to `ConnectionProfile` (`AuthKind`,
  `ClientId`, `TenantId`, `UserName`, cert thumbprint/store, `SecretRef`) plus
  `ToCredentialSpec()`. Added `ISecretStore` / `DpapiSecretStore`. Repointed the MSAL cache
  file name at the full identity. All projects build, 18/18 tests pass.
- **Breaking for existing installs**: the token cache file went from
  `{authorityHost}.{clientId}.msalcache` to
  `{authorityHost}.{kind}.{sha256 of clientId|tenantId|principal}.msalcache`. Everyone signs
  in once more; old files are orphaned, not migrated. Doing it now is cheap, doing it after
  other kinds ship is not.
- Verified out of band (Windows PowerShell 5.1 + `[Reflection.Assembly]::Load([IO.File]::ReadAllBytes(...))`
  so the shell never locks the DLL — pwsh 7 cannot load these net462 assemblies):
  secret round-trips; no plaintext on disk; `null` for unknown and deleted references;
  `ArgumentException` for a `..\..\` reference; three case-varying `CredentialSpec` values
  collapse to two dictionary entries; four identities differing by one field each produce
  four distinct cache file names.
- Security shape worth keeping: profiles roam (`ApplicationData`) and hold only a
  `SecretRef`; secrets are DPAPI `CurrentUser` blobs under `LocalApplicationData`, so a
  copied file decrypts to nothing. Deliberately **not** MscrmTools' shared-passphrase
  `CryptoManager` — an open-source passphrase plus a copied file is a decrypt. A failed
  decrypt returns null so the user is re-prompted rather than seeing a crypto exception.
- Per-connection `ClientId`/`TenantId` now override the host default, which is what allows a
  user connection and a service-principal connection to coexist in one add-in.
- Next (step 4): `ClientSecretCredential`. Needs the `<resource>/.default` scope (public
  clients use `/user_impersonation`) and a `DpapiTokenCache` overload for MSAL's
  `AppTokenCache` — confidential clients do not touch `UserTokenCache`, so without it every
  call re-acquires.

- Prompts: 5
- Summary: Added `tests/DataverseAddIn.Connections.Tests` (xunit, net462) — **58 tests,
  all offline**, covering the step 3 surface: `DpapiSecretStore`, `CredentialSpec`,
  `ConnectionProfile`, `ConnectionStore`, and token-cache partitioning. Combined suite is
  now 76 tests. Made `DataverseAuthOptions.ResolveTokenCacheFilePath()` public — it was
  internal, it is genuinely useful for "which cache file is this connection using?", and
  the alternative was reflection or an `InternalsVisibleTo` shim.
- **Mutation-checked rather than assumed.** 58/58 passing on the first run is exactly when
  tests are most likely to be vacuous, so I reverted the cache-key fingerprint to client ID
  only and re-ran: 3 tests failed, then restored. A green suite that has never been seen red
  proves nothing.
- Tests worth keeping for their intent, not their coverage:
  - `Persists_only_a_secret_reference_never_a_secret` writes a real secret through
    `DpapiSecretStore`, then greps `connections.json` for the plaintext. It fails the moment
    anyone adds a profile property that carries a secret.
  - `A_user_and_a_service_principal_on_one_app_do_not_share_a_cache` pins the exact
    regression step 3 fixed.
  - `Rejects_references_that_are_not_safe_file_names` covers `..\`, `../`, absolute paths,
    spaces and dots, because a secret reference becomes a file name.
  - `Unreadable_ciphertext_reads_as_null` pins the re-prompt-instead-of-crash behaviour for
    a secrets file copied from another machine.
- Gotcha: `dotnet sln add` wrote the **legacy** C# project type GUID
  `{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}` for the new SDK-style project instead of
  `{9A19103F-16F7-4668-BE54-9A1E7A4F7556}` — the same GUID as the VSTO project. Visual
  Studio would have loaded the test project with the legacy project system. Corrected by
  hand. Check the type GUID after every `dotnet sln add` in this repo.
- Confirmed afterwards that `src/DataverseAddIn.Excel/obj` is still free of
  `project.assets.json` and `*.nuget.g.*`.

- Prompts: 6
- Summary: Closed the four coverage gaps before step 4. Suite is now **122 tests**
  (18 ingestion + 104 connections), all offline. Extracted scope construction from
  `DataverseAuthenticator.BuildScope` (internal, untestable) into a public
  `DataverseScope.Delegated` / `.Resource` in `DataverseAddIn.Discovery` — step 4 adds the
  `.default` application variant there, and the delegated behaviour is now pinned first.
  Added `FakeTokenSource`, `FakeCredential`, `DataverseConnectionManagerTests`,
  `AuthKindDescriptorTests`, `DataverseScopeTests`, `GlobalDiscoveryAccessTests`.
- **Production defect found by writing the tests**: `manager.Add(null, url)` did not compile —
  `Add(string, string, string)` and `Add(DataverseInstance, string, string)` are ambiguous on
  a null literal. Fixed at the source by renaming the discovery overload to `AddDiscovered`
  rather than casting in the test. Only two call sites existed. This is the argument for
  writing tests against an API you thought was finished.
- **Mutation-checked in two rounds.** Round one broke three things at once (dropped `Kind`
  from `CredentialSpec.Equals`, made `DataverseScope` return the full path, disabled the
  `SupportsGlobalDiscovery` guard): 12 failures across all three test classes.
  - But the `CredentialSpec` mutation failed only **one** test. `Works_as_a_dictionary_key`
    and the manager separation test both **survived a broken `Equals`**, because
    `GetHashCode` still included `Kind`, so the hash buckets stayed distinct. A dictionary
    test cannot detect an `Equals`/`GetHashCode` divergence on its own.
  - Added `Equality_and_hash_code_never_disagree` — a pairwise contract check over a 36-spec
    matrix — and mutation-checked *it* specifically: with `Kind` dropped from `Equals` it
    fails, alongside the theory row. That is the guard the dictionary tests only appeared
    to provide.
- Lesson worth generalising: "the tests went red" is not enough. Check *which* tests went
  red. A mutation that fails 1 test where you expected 3 is telling you two of them are
  weaker than they look.
- `ConnectAsync` remains untestable and uncovered: `IDataverseCredential.CreateClientAsync`
  returns a concrete `ServiceClient`, which needs a live environment, so `FakeCredential`
  throws from it. Decide before step 4 whether the seam should return an interface or the
  post-connect bookkeeping should be extracted.

- Prompts: 7
- Summary: Resolved the `ConnectAsync` testability question. **Decided against wrapping
  `ServiceClient`** and extracted the decision instead. Added
  `ConnectionProfile.AdoptOrganizationName(string)` — the only branching `ConnectAsync`
  had — and reduced the call site to `if (profile.AdoptOrganizationName(...)) _store.Save();`.
  Added 11 tests: name adoption (placeholder replaced, user-chosen name never overwritten,
  adopt-once, blank org name, trimming) and the reachable parts of `ConnectAsync` (null
  profile, wrong-cloud refusal, failed connect leaves state and profile untouched and raises
  no event). Suite is now **133 tests** (18 + 115).
- **Probed `ServiceClient` rather than assuming it was unfakeable.** It is `sealed: False`,
  which looks promising, but every member the code depends on — `IsReady`,
  `ConnectedOrgFriendlyName`, `RecommendedDegreesOfParallelism`, `EnableAffinityCookie`,
  `Clone()` — is **non-virtual**, and there is no constructor taking an `IOrganizationService`.
  There *is* a `(ConnectionOptions, deferConnection: true, ConfigurationOptions)` ctor that
  constructs offline, but `ConnectedOrgFriendlyName` is get-only and non-virtual, so the one
  branch worth testing still cannot be driven. An `IDataverseConnection` wrapper would have
  been pure indirection across `IngestionEngineFactory` and `DataverseRibbon`.
- **A mutation that fails nothing is usually a bad mutation, not a passing test.** I first
  injected `ConnectionChanged?.Invoke(...)` immediately *after* `CreateClientAsync` — which
  throws in the fake — so the line was unreachable and nothing failed. That proves nothing
  about the tests. Re-injected the realistic regression instead (move `Disconnect()` and the
  state assignment *before* the client is built) and
  `A_failed_connect_changes_nothing_and_announces_nothing` failed as intended.
  Check that a mutation is actually executed before concluding the tests are weak.
- Name-adoption mutation (dropping the `NameIsAuto` guard) failed 2 tests, including
  `A_user_chosen_name_is_never_overwritten` — the user-visible bug that guard prevents.
- What remains uncovered in `ConnectAsync` is now `Disconnect(); Current = client;
  CurrentProfile = profile;` plus one event raise — no branching. Revisit the wrapper
  decision only if retry, fallback-between-kinds, or reconnect-on-expiry is added.

- Prompts: 8
- Summary: **Step 4 — `ClientSecretCredential`, the first kind added since the abstraction
  landed.** Cost: one token source (`ClientSecretTokenSource`), one credential
  (`ClientSecretCredential`), one descriptor entry, and `CredentialFactory` to map kind to
  implementation. **No consumer changed** — which is the whole return on steps 1 and 2.
  Suite is now **164 tests** (18 + 146).
- Key correctness points, all pinned by tests:
  - `DataverseScope.Application` returns `<resource>/.default`; `Delegated` returns
    `/user_impersonation`. Not interchangeable — the delegated scope fails on a
    client-credentials request. A test asserts the two are never equal.
  - Tokens go through the **same token-provider path** as interactive, not the SDK's
    `AuthType=ClientSecret` connection string, so one token serves `ServiceClient` and the
    Web API clients and the cache is shared. Fewer lines the other way, but two code paths.
  - `SupportsGlobalDiscovery => false`. First credential where that flag does real work: a
    service principal has no environments to enumerate, so the UI must ask for a URL.
  - **Multi-tenant authorities rejected at construction.** `DataverseAuthOptions.TenantId`
    defaults to `organizations` — correct for a public client, invalid for client
    credentials. Without the guard the default configuration fails at Entra ID with an
    opaque error. `common` and `consumers` refused too.
- The step-3 `AppTokenCache` note was **half wrong**: no `DpapiTokenCache` overload was
  needed, because `Attach` already takes `ITokenCache` and
  `IConfidentialClientApplication.AppTokenCache` is one. The real requirement was attaching
  to the *right* cache — `UserTokenCache` stays empty for a confidential client, so
  attaching there would silently re-acquire a token on every call.
- Mutation-checked three ways — wrong scope on `Application`, `SupportsGlobalDiscovery`
  flipped to true, and the single-tenant guard short-circuited: **11 failures across five
  test classes**, including `Every_supported_descriptor_has_a_working_implementation`, which
  catches descriptor and implementation drifting apart.
- Had to update `The_interactive_only_manager_refuses_other_kinds` — it asserted
  `ClientSecret` was unsupported, which step 4 makes false. Retargeted at `Certificate`.
  Expected churn: a test that pins "not implemented yet" has to move when it is implemented.
- Next (step 5): nothing can select `ClientSecret` from the UI. `ConnectionDetailsForm` needs
  the descriptor-driven kind picker, `AddDiscovered`/`Add` need to accept a kind, and the
  secret needs capturing into `ISecretStore` at save time.

- Prompts: 9
- Summary: **Step 5 — descriptor-driven connection dialog.** `ConnectionDetailsForm` now
  renders from `AuthKindDescriptor`: a combo over `Supported`, a description/warning line,
  and one row per `AuthField` in `RequiredFields | OptionalFields`. No `switch` on kind
  anywhere in the UI. Added `ConnectionAuthentication` to carry the choice from dialog to
  manager, `Add`/`AddDiscovered` overloads that accept it, and `UpdateAuthentication`.
  Suite is now **173 tests** (18 + 155).
- **Found a shipping bug that unit tests could not have caught.** `ValueOf` decided whether a
  field applied by reading `TextBox.Visible`. `Control.Visible` is false whenever the control
  is not on screen — **including after `ShowDialog` returns**, which is precisely when
  `ConnectionManagerForm` reads `dialog.Authentication`. Every client-secret connection would
  have saved with null client ID, tenant and secret, then failed at connect time with a
  confusing error. Fixed with `AuthFieldRow.IsApplicable`; `Visible` is now output only.
  - Rule: **a WinForms property that reflects runtime state must never store intent.**
  - It was found by constructing the dialog off-screen and printing its state, not by a test.
    Kept as `tools/verify-connection-dialog.ps1`, which now asserts the after-close case
    explicitly and throws on regression. Run it after touching the dialog or adding a
    descriptor. (Earlier this session I deleted a similar probe as un-CI'd clutter; this one
    caught a real defect, so it stays.)
- Secret lifecycle, all pinned by tests: secret goes to `ISecretStore` and never to
  `connections.json`; rotating reuses the reference; a **blank secret on edit keeps the saved
  one** (it cannot be read back to prefill); switching a connection away from `ClientSecret`
  **deletes** the stored secret rather than orphaning it.
- Verified dialog behaviour off-screen: Interactive shows ClientId/TenantId/UserName, all
  optional, OK enabled immediately; ClientSecret shows ClientId/TenantId/ClientSecret and OK
  stays disabled until the third is typed; secret box masked; values survive close.
- The five-step plan is complete. Remaining kinds (`DeviceCode`, `Certificate`,
  `UsernamePassword`, `ExternalToken`, `ConnectionString`, `WindowsIntegrated`, `Ifd`) are now
  one descriptor plus one credential each, with no UI work. Still no CI.

- Prompts: 10
- Summary: **CI.** Added `tools/build-and-test.ps1` and `.github/workflows/build.yml`
  (windows-latest — required, not preferred: net462, WinForms, DPAPI). The script enumerates
  every `.csproj` under src/samples/tests **except `DataverseAddIn.Excel`**, builds each,
  runs both test suites, then runs the connection-dialog check. Verified locally in Release:
  173 tests pass and the dialog check passes, including the exact
  `-Configuration Release -FailOnVstoArtifacts` command CI runs.
- **The contamination guard caught a real one on its first run, from a source I had not
  considered.** The VSTO `obj` folder had `project.assets.json` and `*.nuget.g.*` written at
  10:45, which matched none of my commands — the **VS Code C# language server** loads the
  `.sln`, which includes the VSTO project, and restores it in the background. So this is not
  a "don't run dotnet build on the solution" problem that cleaning once solves; it comes back
  by itself whenever the workspace is open.
  - Design follows from that: **locally the script cleans and reports**, because the editor
    causes it and failing would be noise. **In CI it fails** (`-FailOnVstoArtifacts`), because
    no language server runs there, so anything present is a genuine regression in the
    project exclusions.
- Unverified and worth watching on the first real run: the connection-dialog step constructs
  WinForms controls, which needs a window station. This generally works on hosted Windows
  runners but I cannot confirm it from here. If it fails, pass `-SkipDialogCheck` in the
  workflow and run the check locally instead.
- No git repository exists in this folder yet, so the workflow has never executed.
- Also (prompt 10): **auth kind is now visible in the connection list.** Added a "Sign-in"
  column to `ConnectionManagerForm` between Cloud and Environment URL, and appended the same
  text to the connected status line. Shows the descriptor `DisplayName` plus who it connects
  as — `UserName` for interactive, `ClientId` for a service principal — so a nightly-load
  connection is distinguishable at a glance from a personal one.
  - Uses `AuthKindDescriptor.TryGet` with a `"<Kind> (unsupported)"` fallback, **not** `For`,
    which throws. A profile naming a kind this build does not implement — written by a newer
    build, or hand-edited — must not crash the list.
  - Verified off-screen by rendering the form against a temp store holding four profiles
    (interactive with and without a user name, client secret, and an unregistered
    `Certificate` kind), asserting every row has exactly as many cells as there are columns.
    Column/subitem misalignment in a `ListView` is silent, which is why it was worth checking
    rather than eyeballing.

- Prompts: 11
- Summary: **Documentation restructure.** README went from 571 lines of numbered tutorial to
  ~120 lines of reasoning: the problem, the three shaping decisions (VSTO over web add-in,
  bulk APIs over `ExecuteMultiple`, pluggable auth), quick start, pointers. The how-to moved
  into `docs/architecture.md`, `docs/authentication.md`, `docs/vsto.md`, `docs/ingestion.md`.
  Added `AGENTS.md` plus a thin `.github/copilot-instructions.md` pointing at it.
- Evidence the restructure was overdue: the README contained **two separate
  `### VSTO gotchas` sections** (lines 454 and 500) and never mentioned the credential
  abstraction at all, despite steps 1-5 having added it. Both fixed. Verified all 33 relative
  markdown links resolve, and that no duplicate headings remain anywhere.
- **Declined to use Understand-Anything**, which the user asked about, and said why: its
  pitch is a 200k-line unfamiliar codebase; this is **6,467 lines across 63 files** in five
  deliberately-layered projects, where a structural graph mostly restates the folder names.
  The valuable knowledge here is non-structural and unrecoverable from source - GCC using
  public Entra, `ExecuteMultiple` not being a throughput tool, `Control.Visible` after
  `ShowDialog`, `MSB4044`. Add the first-run token cost and the staleness risk of a committed
  graph and it does not pay yet. Revisit past ~30-50k lines or with contributors who lack the
  decision-record context; its `/understand-onboard` would make a decent contributor-guide
  draft.
- `AGENTS.md` deliberately leads with the traps rather than the architecture, because an
  agent that reads only the first screen should still avoid `dotnet build` on the solution.
- Note: the docs describe the *current* API (`DataverseConnectionManager`, `CredentialSpec`,
  descriptors). The old README examples still used `DataverseAuthenticator` directly, which
  works but is no longer the idiomatic entry point.

- Prompts: 12
- Summary: **Correction from the user.** The docs said the Office/SharePoint workload "is only
  installable in Visual Studio 2019 - the VS 2022 installer lists it in its catalog but
  reports it as unavailable", framed as a VS 2022 limitation. It is not: **it is an ARM64
  limitation.** On x64, VS 2022 has the workload. This machine is Windows on ARM, where VS
  2022 reports it unavailable and only the emulated VS 2019 can install it.
- Fixed in `docs/vsto.md` (prerequisites, building, running), `README.md`, and as a dated
  correction inside `decisions/0005-platform-choice.md`. Also generalised "VS 2019 fails with
  ... RuntimeIdentifier" to "Visual Studio", since that failure is not version-specific.
  Left the SESSION_LOG history intact - wrong turns are recorded, not rewritten.
- Lesson, and it is the second time this class of error has appeared here: **an observation
  from one machine is not a property of the tool.** The earlier version of this same note was
  already a correction (I had claimed the workload *was* available in VS 2022 because it
  appeared in the installer catalog). Both errors came from reporting local environment state
  as general truth. When a constraint is discovered on this machine, say which machine
  property causes it - ARM64, emulation, an installed SDK - or say it is unverified elsewhere.

- Prompts: 13
- Summary: Expanded the platform decision. The user asked whether we actually explain *why*
  VSTO over the newer Office Web Add-in, how we got there, and what was gained and lost.
  We did - but thinly: the narrative was one line and there was no ledger of costs.
  `decisions/0005-platform-choice.md` now has **How we got here** (the two reversals),
  **What we gained**, **What we gave up**, and a trigger table for **What would change this
  decision**. README decision #1 rewritten to carry the same arc and, importantly, the losses.
- The honest part worth keeping: I recommended the web-add-in-plus-backend option **before
  knowing the volume**. The throughput reasoning was correct in the abstract and irrelevant
  at 20,000 rows. Recorded as a mistake, not smoothed over.
- Losses now stated plainly rather than implied: Windows-only; **no central deployment** (per
  machine install and a signing cert versus a push from the M365 admin centre); VSTO is the
  older model and is already unsupported in the new Outlook; smaller sample/developer pool;
  contributor friction from the Office workload. Previously only "Windows-only" appeared.
- Gains that had never been written down: stronger auth (system browser, so FIDO/Windows
  Hello/device-compliance CA all work; DPAPI token cache; WAM as an upgrade path - a task
  pane gets none of these), no compliance surface, and no structural throughput ceiling.
- Fixed a dangling `[spr]` reference definition orphaned by the rewrite, and added a docs
  check for undefined *and* unused reference-style links - an undefined one renders as
  literal text and is easy to miss in review. 33 links, 0 broken.

- Prompts: 14
- Summary: Added the **Test connection** button - the one genuine UI gap remaining. Also
  corrected two claims I had made from memory rather than from the code: connect failures
  were already handled (status line in Firebrick, not a raw message box), and discovery
  gating was not actually broken, since `DiscoverAsync` uses `CredentialSpec.ForDiscovery`
  (always interactive) and the ClientSecret warning already renders in the dialog.
  Suite now 176 tests (18 + 158).
- Shape: `CredentialFactory.Create(spec, secretOverride)` lets a credential be built from a
  secret the user has typed but not saved, so **testing never writes to the secret store**.
  `DataverseConnectionManager.TestAsync` builds that credential *outside* the cache, connects,
  runs `WhoAmI`, and disposes. Connecting proves the identity resolves; WhoAmI proves the
  account exists in that environment and holds a role - which is exactly what fails for a
  service principal with no application user, and the reason the button exists.
- The dialog takes an optional tester delegate rather than a manager, so it stays usable
  standalone and the caller can close over the saved `SecretRef` for the "blank keeps the
  saved secret" case.
- **The verification harness fell into the same `Control.Visible` trap it exists to catch.**
  I asserted `_test.Visible` to prove the button shows/hides - but `Visible` is false on a
  form that was never shown, so the assertion printed the expected value for the wrong reason
  and would never have failed. Replaced with a check on the `_tester` field. Second lesson:
  `Button.PerformClick()` is a **no-op on an unshown form** (`CanSelect` is false), so the
  click path had to be invoked through reflection instead.
- New checks pinned: a failing test shows the reason and **leaves OK enabled** - a failed
  test must not wedge the dialog.

- Prompts: 15
- Summary: Added a **VSTO custom task pane** (yes, VSTO supports dockable/floating panels) and
  recorded the **origin story** in the README. 176 tests still pass; the VSTO project builds
  clean under VS 2019 MSBuild.
- Origin story: a friend, **Tim**, asked whether Entra ID app registrations are the same across
  clouds - he was building something similar. The answer (GCC = public Entra, GCC High/DoD =
  Entra Government, so a commercial registration is invisible there) is precisely why the
  cloud/identity split is modelled so carefully in this codebase. README now opens with that,
  and frames the decision records as the long-form version of the reply.
- Task pane shape: `DataversePaneControl` is a plain `UserControl` in `DataverseAddIn.WinForms`
  (host-agnostic); `DataverseTaskPanes` in the add-in is the only Office-aware part. Ribbon
  gains a `toggleButton` with `getPressed`, the documented way to keep pane and button in sync.
- Four non-obvious VSTO facts, all from the Microsoft docs rather than memory:
  - A pane is bound to **one document frame window**. Excel 2013+ is SDI, so one pane per
    workbook window, keyed by `Window.Hwnd`, created on demand - otherwise it vanishes when
    the user switches workbooks.
  - Panes start hidden and Office provides **no way to reopen** a closed one; you must supply
    the control.
  - **Never `Remove`/`RemoveAt` in `ThisAddIn_Shutdown`** - the runtime disposes panes first
    and it throws `ObjectDisposedException`. Cleanup happens on workbook deactivate instead.
  - `Width` only applies when docked left/right.
- **The alias trap predicted in the docs actually bit.** `using Excel = Microsoft.Office.Interop.Excel;`
  does not compile in this project: the namespace `DataverseAddIn.Excel` shadows the alias, so
  `Excel.Window` binds to `DataverseAddIn.Excel.Window` -> `CS0234`. Renaming the project
  namespace earlier did not help because its last segment is still `Excel`. Fixed with an
  `ExcelInterop` alias and documented in docs/vsto.md.
- Not runtime-verified: the pane compiles and is wired, but Excel could not be launched here.
  The per-window behaviour in particular deserves a manual check on first F5.

- Prompts: 16
- Summary: **Renamed `DataverseAddIn.Excel` to `DataverseAddIn.ExcelHost`** and documented the
  rule. A namespace segment shadows a using-alias of the same name, so
  `using Excel = Microsoft.Office.Interop.Excel;` was silently ignored inside
  `namespace DataverseAddIn.Excel` and `Excel.Window` bound to `DataverseAddIn.Excel.Window`.
  18 files rewritten, folder/csproj/pfx moved with `git mv`, VSTO rebuild clean, 176 tests
  pass, 36 doc links resolve.
- **Why rename rather than keep the `ExcelInterop` workaround:** in a normal project the alias
  convention is fine. In a *template* it is a recurring tax - nearly every Office sample uses
  `Excel.Range` / `Excel.Worksheet` / `Excel.Workbook`, so every paste fails with a `CS0234`
  that never mentions aliases. A starter should model the practice, not the trap.
- Name chosen for consistency with the existing `Samples.ConsoleHost` / `Samples.WinFormsHost`
  vocabulary. **Only the last segment matters** - `DataverseAddIn.Host.Excel` would still
  collide. `Office` is the same trap, since the ribbon aliases `Microsoft.Office.Core`.
- Proof the fix works: `DataverseTaskPanes` now uses the plain `Excel` alias and compiles.
  Had I only renamed and not reverted the alias, nothing would have been demonstrated.
- Three things had to move together or the build breaks confusingly: `RootNamespace`, the
  `GetResourceText("<RootNamespace>.DataverseRibbon.xml")` string, and `ManifestKeyFile`
  (the .pfx was renamed to match). Verified all three by inspection before building.
- Cleaned up the orphaned `HKCU:\...\Excel\Addins\DataverseAddIn.Excel` registration, which
  pointed at a `.vsto` under the old path; MSBuild registered `DataverseAddIn.ExcelHost` on
  the next build. Also deleted the moved-along `bin`/`obj` so two `.vsto` files did not sit in
  one output folder.
- Excluded `SESSION_LOG.md` from the bulk replace on purpose - it is a dated record and should
  keep saying what was true at the time. The bulk replace *did* corrupt the explanation prose
  in `docs/vsto.md` (it rewrote "the enclosing `Excel` segment" into `ExcelHost`, making the
  paragraph self-contradictory); rewrote that section by hand afterwards. Worth remembering:
  a repo-wide rename silently damages any prose that discusses the old name.

- Prompts: 17
- Summary: **Added certificate authentication** - the third credential kind. Cost was exactly
  the four steps the abstraction promises (`CertificateTokenSource`, `CertificateCredential`,
  descriptor entry, `CredentialFactory` case) plus one dialog field. `AuthField.CertificateThumbprint`
  and the `ConnectionProfile` certificate columns were already in place from step 3, so nothing
  in the model had to change. 190 tests (18 + 172).
- Security shape worth noting: this is the first kind where **the add-in never holds the
  credential at all**. The private key stays in the Windows certificate store; only a
  thumbprint is persisted. Nothing in `connections.json`, nothing in the DPAPI secret store.
  Switching a connection to Certificate deletes any previously stored secret - pinned by a test.
- Three guards, each because the default failure sends people hunting the wrong bug:
  - **Thumbprint normalisation.** Pasted from certmgr it carries spaces, lower case, and an
    invisible U+200E left-to-right mark on the first character; `X509Store.Find` matches none
    of those and reports "not found". Normalised to bare upper-case hex.
  - **No private key** - importing the `.cer` rather than the `.pfx`. Detected with advice.
  - **Not found** - the error names the stores searched and the normalised thumbprint.
  - Store search order is `CurrentUser\My` then `LocalMachine\My`, with an inaccessible
    LocalMachine store skipped rather than treated as an error (common without elevation).
- **Could not unit test the constructor guards**, and said so in the test file rather than
  quietly omitting them: building a certificate in memory needs `CertificateRequest`, which
  arrived in .NET Framework **4.7.2** and does not exist on this project's net462. The
  alternative - committing a test PFX - would put a private key in the repository for the sake
  of a test, immediately after a conversation about why that is wrong. Covered the pure logic
  instead.
- Four tests failed on the way through, all of them pinning "Certificate is not implemented".
  Same churn class as when ClientSecret landed; retargeted at `DeviceCode`/`Ifd`. Also relaxed
  `Every_supported_descriptor_has_a_working_implementation`: it now asserts the factory does
  not throw `NotSupportedException`, tolerating environment-dependent failures, because one
  supported kind now needs a certificate actually installed.
- Dialog verified off-screen: the picker offers three kinds, Certificate swaps the field set to
  ClientId/TenantId/CertificateThumbprint, OK stays disabled until the thumbprint is entered,
  and switching away from ClientSecret clears the typed secret.

- Prompts: 18
- Summary: Added a **certificate picker** to the connection dialog - a Select... button opening
  the Windows `X509Certificate2UI` dialog, plus a live detail line resolving the thumbprint to
  a subject and expiry. `AuthFieldRow` gained an optional trailing button so the layout stays
  descriptor-driven rather than special-casing one field. 196 tests (18 + 178).
- **The probe caught a real UX bug before any user did.** The first version of `FindUsable`
  returned everything with a private key: **1018 certificates on this machine**. Breaking it
  down by store showed `CurrentUser\My` held 1008, of which the overwhelming majority were
  Fiddler `DO_NOT_TRUST` interception certificates - a normal state for any developer machine
  that has ever run a debugging proxy. A picker with a thousand rows is unusable.
  - Fixed by filtering on the **validity window**, which is principled rather than a heuristic:
    an expired certificate cannot authenticate, so nothing usable is hidden. 1018 -> 68.
    Sorted freshest first. Resisted the temptation to pattern-match on "DO_NOT_TRUST" or to
    filter by EKU - MSAL does not enforce EKU for app credentials, so that would have hidden
    certificates people legitimately use.
  - Two tests now pin the contract (`HasPrivateKey`, inside the validity window, sorted).
    They assert over whatever is on the machine, so they stay meaningful anywhere.
- `TryFind` added as `Find` without the exception, so the UI can report as the user types
  instead of driving control flow through exceptions.
- Live feedback also catches the wrong-thumbprint case *before* Test connection: paste
  something absent and the line turns orange immediately.
- Lesson worth keeping: **enumerate-the-real-machine probes find UX bugs that unit tests
  cannot.** Nothing about `FindUsable` was incorrect - it did exactly what it said. It was
  simply unusable against real store contents, and only running it revealed that.

- Prompts: 19
- Summary: Answered "what happens if the user closes the OAuth browser without signing in"
  and fixed it. Answer: it hangs forever. With the system browser MSAL waits on a loopback
  `HttpListener` for the redirect, and a closed browser sends that listener nothing - there is
  no signal and no built-in timeout. Verified all three UI call sites awaited unbounded
  (`ConnectAsync(profile)`, `DiscoverAsync(cloud)`, `_tester(..., CancellationToken.None)`),
  so the dialog sat on "Connecting..." with every button disabled, indefinitely.
- Added `InteractiveSignIn.RunAsync` in Discovery: links the caller token with a deadline and
  translates only the deadline's cancellation into `SignInCanceledException`, leaving a
  caller-requested cancellation as `OperationCanceledException`. `DataverseAuthOptions`
  gained `InteractiveTimeout` (5 minutes, `Timeout.InfiniteTimeSpan` to disable).
- Also mapped `MsalClientException` / `MsalError.AuthenticationCanceledError` to the same
  exception. That is the *embedded* WebView's closed-window signal - not the path we use, but
  the two browsers should not report the same user action differently.
- `ConnectionManagerForm` now styles a cancelled sign-in as gray information rather than
  firebrick error. Nothing failed; the user changed their mind.
- Mutation check worth recording: replacing the deadline with `InfiniteTimeSpan` did **not**
  turn the tests red - it **hung the test run**, which is precisely the symptom the user
  reported. A hang is a legitimate mutation result when the thing under test is a timeout,
  but it means the suite has no upper bound of its own; the run had to be killed manually.
- 206 tests (18 + 188). 10 new, including that a caller cancellation is never reported as a
  timeout, and that the deadline actually reaches the sign-in rather than only wrapping it.
- Still open: there is no Cancel affordance during those five minutes. The timeout stops the
  infinite hang, it does not let the user escape early.

- Prompts: 20
- Summary: Added a Cancel affordance so an unfinished sign-in can be abandoned rather than
  holding the window for the full five minutes. The manager already took a `CancellationToken`
  on `ConnectAsync` / `DiscoverAsync` / `TestAsync`, and `DataverseServiceClientFactory` already
  passed it to the interactive pre-warm, so the gap was entirely in the UI - all three call
  sites simply passed nothing.
- `CancelableButton` turns the starting button into its own cancel button. Reused the existing
  button rather than adding a second one, because all three forms lay buttons out in fixed grid
  cells; and kept the existing Click handler, with the handler calling `CancelIfRunning()`
  first, because WinForms gives no way to detach an arbitrary handler and restore it. `Begin()`
  returns a scope that swaps caption and glyph and restores both on dispose.
- Ordering that matters: `Begin()` runs *after* the form disables its controls, because it
  deliberately re-enables the one button it owns.
- Wired into `ConnectionManagerForm` (Connect), `DiscoveryPickerForm` (Load environments) and
  `ConnectionDetailsForm` (Test connection). All three now separate a cancellation from a
  failure in the status text - gray, not firebrick.
- Verified in `verify-connection-dialog.ps1` rather than as a unit test, following the existing
  convention for WinForms. Mutation-checked by dropping `_running.Cancel()`: the script caught
  it (`token cancelled=False` -> REGRESSION), which is a proper red rather than the hang the
  timeout mutation produced last time.
- `CancelableButton` is public rather than internal so the verification script can construct it,
  matching `FormScaling` and `Glyphs`.
- Not verified: that cancelling actually interrupts MSAL''s loopback wait against a real browser.
  It shares a code path with the timeout, so the two stand or fall together, and neither has yet
  been exercised against live MSAL.

- Prompts: 21
- Summary: Confirmed by hand that Cancel interrupts a real MSAL sign-in immediately - the one
  thing reasoning could not settle. Then renamed "Add from discovery..." to
  **"Find my environments..."** ("discovery" was Global Discovery Service jargon) and made the
  interactive-only restriction visible instead of a surprise.
- Derived the restriction rather than hard-coding it: `AuthKindDescriptor` already carried
  `SupportsGlobalDiscovery`, so added `DiscoveryCapable` and a `DiscoveryRequirement` sentence
  built from the registry. Adding a discovery-capable kind updates the UI text by itself. The
  sentence leads with the alternative, since what to do instead matters more than the rule:
  "...so this needs "Sign in interactively". For an application user, add the environment by
  URL instead."
- Shown in two places: a tooltip on the button, and as the picker''s opening status line, so it
  is read before a sign-in rather than after it fails. Dialog retitled to match the button.
- Mutation check that came back **negative, and is worth recording**: sourcing `DiscoveryCapable`
  from `IsInteractive` instead of `SupportsGlobalDiscovery` did not fail a single test. Not a
  weak test - the mutation is *equivalent today*, because no registered kind separates the two
  flags (Interactive is true/true, both application-user kinds are false/false). It stops being
  equivalent the moment a kind differs. Kept the tests; the finding is that they cannot yet
  distinguish the two, not that they are wrong.
- 209 tests (18 + 191), 3 new.
- Two build-lock traps hit in one session, both self-inflicted: the sample host I launched for
  testing locked the libraries, and then `[Reflection.Assembly]::LoadFrom` in the *persistent*
  terminal locked them again - .NET cannot unload an assembly, so that shell could never build
  again and had to be exited. `verify-connection-dialog.ps1` is the right place for anything
  that needs to load these DLLs, because `pwsh tools/...` runs it in a child process that exits.

- Prompts: 22
- Summary: Investigated how an air-gapped/sovereign cloud could supply its own Global Discovery
  endpoint without those endpoints being shipped in the app. Design only - deferred, nothing
  implemented.
- Verified, and worth keeping: `ConfigurationManager` binds to the **host process**, not to the
  loaded assembly (probe: `OpenExeConfiguration(...).FilePath` was unchanged after `LoadFrom`
  of one of our DLLs). That looked at first like a bug in `ThisAddIn.BuildAuthOptions`, whose
  comment claims the add-in reads its own `.dll.config` - but the probe only demonstrates the
  *default* AppDomain. VSTO loads each add-in into its own AppDomain with `<addin>.dll.config`
  as that domain''s configuration file, so the comment is correct and the `ClientId.<Cloud>`
  settings do work in Excel. **Near-miss recorded deliberately: a probe that models the wrong
  host proves nothing about the real one.**
- Recommendation, if picked up later: a `clouds.json` beside `connections.json` in the roaming
  profile, not app.config. ClickOnce replaces deployed files on update, buries them under
  `%LOCALAPPDATA%\Apps\2.0\<mangled>`, and hashes them for integrity; and pre-seeding one in the
  installer would put the sensitive endpoints back into the build, which is the thing being
  avoided. A roaming-profile file survives updates, needs no admin rights, and can be pushed by
  GPO. An entry needs authority host, ClientId/TenantId and URL suffixes, not just the
  discovery URL - a sovereign cloud has its own Entra directory and its own app registration.
- Blocker to weigh first: `DataverseCloud` is an enum used as a lookup key in 19 files
  (`CredentialSpec`, token-cache filename, `ConnectionProfile`), so a user-defined cloud means
  replacing it with a value object. Mitigating: `ConnectionProfile` already persists the cloud
  as a string, so the on-disk format would not change. A smaller alternative was noted - a
  single `Custom` enum member fed from the file, which supports one extra cloud per user for a
  fraction of the churn.
- No decision record written, because nothing was decided.

- Prompts: 23
- Summary: First run of the task pane in Excel found two problems: it opened very narrow, and the
  environment URL was almost unreadable.
- Corrected an assumption before acting on it. The obvious theory was "Width is ignored while
  the pane is hidden", but the official example sets `Width` *before* `Visible`, so that theory
  is unsupported. What the docs do say: **`Width` is in points, not pixels**; it throws
  `COMException` when the pane is docked top or bottom; setting it inside a `VisibleChanged`
  handler also throws - which rules out the workaround usually suggested for this symptom.
  Left the ordering alone, named the constant `DefaultWidthInPoints`, and wrote the units down.
- Legibility had two causes, both certain from reading: `_detail` used `SystemColors.GrayText`,
  and the control inherited its background from the pane host. Now states
  `BackColor = SystemColors.Window` and gives the URL full-contrast `ControlText` when
  connected, keeping gray for the "not connected" hint. Added a tooltip with the full URL.
- Both labels had `MaximumSize = new Size(260, 0)` hard-coded, so widening the pane bought no
  extra text - the wrap width now tracks `ClientSize` in `OnResize`. That is likely part of why
  the pane "felt" small even after being widened.
- Built the VSTO project with VS 2019 MSBuild to confirm it compiles (`build-and-test.ps1`
  excludes it by design), and checked `git status` afterwards for stray obj artifacts and the
  local `ManifestCertificateThumbprint` edit. Clean.
- Not verified: how wide 300 points actually renders, and whether Office overrides it with a
  remembered width. Needs another F5.

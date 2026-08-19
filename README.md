# Dataverse Excel VSTO add-in — starter sample

A working Excel VSTO add-in that signs a user in with OAuth 2.0 (authorization code +
PKCE), lists environments via the [Global Discovery Service][gds], manages saved
connections, and pushes spreadsheet rows into Dataverse at the throughput the environment
allows. Targets **.NET Framework 4.6.2**, matching the Dataverse SDK.

Fork it as a starting point: the libraries under `src/` are host-agnostic, so only
`DataverseAddIn.Excel` is Excel-specific.

## Layout

| Project | Purpose |
| --- | --- |
| `src/DataverseAddIn.Discovery` | MSAL auth + Global Discovery + a minimal Web API caller. Only dependency is `Microsoft.Identity.Client` — 8 DLLs. |
| `src/DataverseAddIn.Connections` | `IOrganizationService` via the SDK's `ServiceClient`, plus the connection manager and saved-connection store. |
| `src/DataverseAddIn.Ingestion` | Bulk ingestion engine and sheet mapper. Depends only on `Microsoft.Xrm.Sdk`, so it is unit testable offline and reusable from any host. |
| `src/DataverseAddIn.WinForms` | WinForms dialogs: connection manager, connection details, discovery picker. Host-agnostic. |
| `src/DataverseAddIn.Excel` | The VSTO add-in itself — ribbon, `ThisAddIn`, and the only Excel-specific project. |
| `samples/DataverseAddIn.Samples.ConsoleHost` | Console harness for the discovery and connection flows. |
| `samples/DataverseAddIn.Samples.WinFormsHost` | WinForms exe mimicking the Excel ribbon, so the UI can be exercised without Office. |
| `tests/DataverseAddIn.Ingestion.Tests` | 18 unit tests for the ingestion engine and sheet mapper. No network, no credentials. |
| `tests/DataverseAddIn.Connections.Tests` | 155 unit tests for credential identity, the credential factory, the connection manager, the saved-connection store, the DPAPI secret store, OAuth scopes, and token-cache partitioning. No network, no credentials. |

Design decisions and their rationale are in [decisions/](decisions).

## Approach

Two ways exist to call Global Discovery from .NET, and this sample uses the first:

1. **MSAL.NET + `HttpClient` against the OData endpoint** — what's implemented here.
2. `ServiceClient.DiscoverOnlineOrganizationsAsync` from `Microsoft.PowerPlatform.Dataverse.Client`.

Option 1 was chosen because a VSTO add-in shares one AppDomain with Excel and every
other installed add-in, so each extra assembly is a potential binding-redirect failure.
`Microsoft.PowerPlatform.Dataverse.Client` pulls in a large dependency graph
(`Microsoft.Extensions.*`, `Newtonsoft.Json`, `System.Text.Json`, WCF stacks). It also
**ignores `$select` and `$filter`** and reshapes results into `OrganizationDetail`
rather than the richer `Instance` type. See
[decisions/0001-discovery-approach.md](decisions/0001-discovery-approach.md).

JSON is handled by the in-box `DataContractJsonSerializer`, so the library's only
NuGet dependency is `Microsoft.Identity.Client`.

If you later want option 2, the overload to use is
`DiscoverOnlineOrganizationsAsync(Func<string, Task<string>> tokenProviderFunction, ...)` —
pass `DataverseAuthenticator.AcquireDiscoveryTokenAsync` into it. Do **not** use the
username/password overloads; they break under MFA and Conditional Access.

## 1. Register the Entra ID application(s)

### Do I need my own app registration?

For local experimentation, no. Microsoft publishes a preconfigured sample public client
that the [OAuth docs][oauth] use in their examples:

| | |
| --- | --- |
| Client ID | `51f81489-12ee-4a9e-aaae-a2591f45987d` |
| Redirect URI | `http://localhost` |
| Authority | `AadAuthorityAudience.AzureAdMultipleOrgs` |

For anything you ship, yes. From the same page:

> The samples we provide are preconfigured with appropriate registration values so that you
> can run them without generating your own app registration. **When you publish your own
> apps, you must use your own registration values.**

Per cloud:

- **Commercial** — works. It is registered multitenant in public Entra ID.
- **GCC** — works. GCC authenticates against public Entra ID too; only the discovery
  endpoint and token audience differ.
- **GCC High / DoD** — the app *does* resolve in Microsoft Entra Government. Verified
  against a real GCC High tenant: the device-code endpoint issued a code for the sample
  client ID and a `<org>.crm.microsoftdynamics.us/user_impersonation` scope, while a
  deliberately invalid client ID returned `AADSTS700016` from the same endpoint.
  Outright app-level blocking of this client ID is unlikely in practice — it is the ID
  baked into XrmToolBox and much of the Dataverse community tooling, so blocking it breaks
  the admins' own tools. Expect *conditional* access rather than denial: MFA, a compliant
  or hybrid-joined device, or a named-location rule that permits corporate network and AVD
  sessions while blocking everything else.

  It still isn't a shipping answer — Microsoft requires your own registration for published
  apps, and consent policy (below) is the failure you are more likely to hit.

### What actually fails, and how to read it

| Symptom | Meaning | Fix |
| --- | --- | --- |
| `AADSTS700016` | App not found in that directory | Wrong cloud, or register the app there |
| `AADSTS65001` / `AADSTS90094` | Consent required, user consent disabled | Tenant admin grants admin consent once |
| `AADSTS53003` | Blocked by Conditional Access | Satisfy the policy — device, network, MFA |
| `AADSTS50076` | MFA required | Expected; the system browser handles it |

In a locked-down tenant, consent policy is the usual blocker, not app blocking. Many
government tenants disable user consent entirely, so the first sign-in for an app whose
service principal is not already provisioned fails with `AADSTS65001` until an admin
consents. Our device-code probe proves the app *resolves*; it does not prove a consented
service principal exists in the tenant.

This is also where the browser choice stops being cosmetic. Conditional Access rules that
test device compliance or hybrid join need device authentication, which the .NET Framework
embedded WebView1 cannot do — the same account that signs in fine in the system browser can
fail `AADSTS53003` in an embedded view. That is why `UseEmbeddedWebView` defaults to
`false`. For AVD or managed desktops where CA leans on device state, the WAM broker is
worth testing (`Microsoft.Identity.Client.Broker` + redirect URI
`ms-appx-web://Microsoft.AAD.BrokerPlugin/{client_id}`); it is the only option here with
first-class device-signal support.

### Probing an environment without credentials

Two unauthenticated checks are worth knowing when onboarding a new tenant or cloud.

**Which authority, tenant and audience does this environment want?** Call any Web API
endpoint with no token and read the challenge:

```powershell
curl.exe -s -i https://<org>.crm.microsoftdynamics.us/api/data/v9.2/WhoAmI
# WWW-Authenticate: Bearer authorization_uri=https://login.microsoftonline.us/<tenantId>/oauth2/authorize,
#                          resource_id=https://<org>.crm.microsoftdynamics.us/
```

`resource_id` is the exact audience, so the public-client scope is that value plus
`user_impersonation`. `DataverseAuthenticator` derives the same string from `ApiUrl`.

**Does a client ID exist in that directory?** Post to the device-code endpoint:

```powershell
curl.exe -s -X POST https://login.microsoftonline.us/<tenantId>/oauth2/v2.0/devicecode `
  -d "client_id=<clientId>&scope=https%3A%2F%2F<org>.crm.microsoftdynamics.us%2Fuser_impersonation"
```

An unknown app returns `AADSTS700016`. Do **not** use `GET /authorize` for this — Entra
defers app resolution there and happily serves a sign-in page for a client ID that does not
exist, so it always looks like success.

### Registering your own

**How many registrations you need depends on the clouds you target.** Dynamics 365 GCC
uses *public* Microsoft Entra ID; GCC High and DoD require *Microsoft Entra Government*,
which is a physically separate directory. A commercial app registration is invisible
there, so it cannot be reused.

| Clouds | Register in | Portal |
| --- | --- | --- |
| Commercial + GCC | Public Microsoft Entra ID | `portal.azure.com` |
| GCC High + DoD | Microsoft Entra Government | `portal.azure.us` |
| China | Entra operated by 21Vianet | `portal.azure.cn` |

The steps are identical in each portal:

1. **App registrations → New registration**
   - Supported account types: **Accounts in any organizational directory** (multitenant)
     if the add-in ships to other tenants, otherwise single tenant.
   - Redirect URI: **Public client/native (mobile & desktop)** → `http://localhost`
2. **Authentication** → *Advanced settings* → **Allow public client flows: Yes**.
3. **API permissions** → *Add a permission* → **Dynamics CRM** →
   *Delegated permissions* → **user_impersonation** → *Add*.
   This single permission covers both the Global Discovery Service and every environment
   in that cloud.
4. Copy the **Application (client) ID**.

No client secret. A desktop/Office add-in cannot keep one.

## 2. Run the console sample

Set `Clouds` and the matching `ClientId` values in
`samples/DataverseAddIn.Samples.ConsoleHost/App.config`, then build the whole solution
with VS 2019's MSBuild (see [Building](#building-the-vsto-add-in) for why not `dotnet build`):

```powershell
& "C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe" `
  DataverseExcelAddIn.sln /restore /t:Build /p:Configuration=Debug /m

.\samples\DataverseAddIn.Samples.ConsoleHost\bin\Debug\net462\DataverseAddIn.Samples.ConsoleHost.exe
```

`App.config` is copied to `DataverseAddIn.Samples.ConsoleHost.exe.config` at build time, so rebuild
after changing it.

> The solution is a classic `.sln`, not the `.slnx` that `dotnet new sln` produces by
> default on the .NET 10 SDK. Visual Studio's VSTO project types are safest in `.sln`. If
> you ever regenerate it, pass `--format sln`.

It signs in per identity authority, lists environments from every configured cloud, and
runs `WhoAmI` against the one you pick — confirming the discovery token *and* the
per-environment token for that cloud.

## 3. Targeting Commercial, GCC and GCC High together

Build one `DataverseAuthenticator` per cloud and hand them to `MultiCloudDiscoveryClient`:

```csharp
var authenticators = new[]
{
    // Public Entra ID app registration — one sign-in covers both of these.
    new DataverseAuthenticator(new DataverseAuthOptions
    {
        ClientId = publicClientId, Cloud = DataverseCloud.Commercial
    }),
    new DataverseAuthenticator(new DataverseAuthOptions
    {
        ClientId = publicClientId, Cloud = DataverseCloud.UsGovernmentCommunity
    }),

    // Entra Government app registration — separate directory, separate sign-in.
    new DataverseAuthenticator(new DataverseAuthOptions
    {
        ClientId = govClientId, Cloud = DataverseCloud.UsGovernmentHigh
    })
};

using (var discovery = new MultiCloudDiscoveryClient(authenticators))
{
    var result = await discovery.GetInstancesAsync(filter: "State eq 0");

    // e.g. the user has no account in Entra Government — warn, don't fail
    foreach (var failure in result.Failures)
        Log(failure.Cloud, failure.Error);

    // instance.Cloud tells you which authenticator to use for its environment token
    foreach (var instance in result.Instances)
        Bind(instance);
}
```

What this buys you:

- Discovery is **one call per cloud**, always — even when clouds share identity. GCC has
  its own endpoint (`globaldisco.crm9.dynamics.com`) and its own token audience.
- Sign-in is **one prompt per identity authority**. Commercial and GCC resolve from the
  same cached account; GCC High prompts separately.
- A cloud the user has no account in lands in `Failures` instead of breaking the list.
  Expect this routinely — few users hold both a commercial and a government identity.
- `DataverseInstance.Cloud` is stamped on every result, so
  `authenticators.First(a => a.Cloud == instance.Cloud)` is all you need to get an
  environment token later. `DataverseCloudExtensions.TryGetCloudFromUrl` recovers the same
  mapping from a persisted environment URL.

## 4. Get an IOrganizationService

`DataverseAddIn.Connections` builds a `ServiceClient` — the SDK's `IOrganizationService` — reusing
the sign-in already performed for discovery. There is no second prompt: MSAL just issues a
token for the new resource.

It uses the `ServiceClient(Uri, Func<string,Task<string>>, bool, ILogger)` constructor,
where "authentication is managed by the caller". Avoid the username/password and
`ClientCredentials` overloads; they break under MFA and Conditional Access.

**From a discovery result:**

```csharp
var instance = result.Instances.First(i => i.FriendlyName == "RAW");
var authenticator = authenticators.First(a => a.Cloud == instance.Cloud);

using (var service = await DataverseServiceClientFactory.CreateAsync(authenticator, instance))
{
    var who = (WhoAmIResponse)service.Execute(new WhoAmIRequest());
}
```

**From a URL the user typed, skipping discovery entirely:**

```csharp
var environment = DataverseEnvironmentReference.Parse("contoso.crm.microsoftdynamics.us");
// environment.Cloud => UsGovernmentHigh, inferred from the host

var authenticator = new DataverseAuthenticator(new DataverseAuthOptions
{
    ClientId = clientId,
    Cloud    = environment.Cloud
});

using (var service = await DataverseServiceClientFactory.CreateAsync(authenticator, environment))
{
    ...
}
```

`DataverseEnvironmentReference.TryParse` accepts what users actually paste — no scheme, a
trailing slash, or a full `/main.aspx?...` URL copied from the browser — and reduces it to
scheme + host. `CloudWasRecognized` is `false` when the host matches no known Dataverse
suffix, which usually means a typo. Both the app host
(`org.crm.dynamics.com`) and the API host (`org.api.crm.dynamics.com`) work as the
environment URL and as the token audience.

The console sample takes a URL as an argument to exercise this path:

```powershell
.\DataverseAddIn.Samples.ConsoleHost.exe contoso.crm.microsoftdynamics.us
```

### ServiceClient in VSTO — the deadlock

`ServiceClient` calls the token provider **synchronously** from inside its own plumbing.
If that call has to do interactive sign-in while running on Excel's STA UI thread, Excel
deadlocks. `CreateAsync` avoids it by acquiring the token on the async path *first*, so the
provider only ever hits the MSAL cache, and by constructing the client inside `Task.Run`.

Two more rules for the add-in:

- Never `.Result` / `.Wait()` a `CreateAsync` call from a ribbon handler — `await` it.
- `useUniqueInstance: true` is deliberate: without it, `ServiceClient` reuses cached
  connections, and an add-in that switches environments can silently keep talking to the
  previous one. Use `service.Clone()` for parallel work rather than sharing one instance.

## 5. Use it from the Excel VSTO add-in

Try the UI first without Office:

```powershell
.\samples\DataverseAddIn.Samples.WinFormsHost\bin\Debug\net462\DataverseAddIn.Samples.WinFormsHost.exe
```

`DataverseAddIn.Samples.WinFormsHost` has the same two commands the ribbon does — **Connections…** and **Who Am I**,
the latter disabled until connected — so the whole flow is exercisable before Office is
involved.

### Building the VSTO add-in

The add-in needs the **Office/SharePoint development** workload. On this machine it is only
installable in **Visual Studio 2019** — the VS 2022 installer lists the workload in its
catalog but reports it as unavailable.

#### First, generate a signing key

The repository deliberately contains **no** `DataverseAddIn.Excel_TemporaryKey.pfx`, so the
first build of a fresh clone fails:

```
error MSB3323: Unable to find manifest signing certificate in the certificate store.
```

Generate your own:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\new-signing-key.ps1
```

That creates an RSA 2048 / SHA-256 code-signing certificate in `Cert:\CurrentUser\My`, exports
it to the path the project expects, and rewrites `<ManifestCertificateThumbprint>` in
`DataverseAddIn.Excel.csproj` to match. **That `.csproj` edit is local to you — do not commit
it.** The thumbprint cannot simply be removed from the project: VSTO's `ManageCertificateStore`
task requires it, and dropping it fails with `MSB4044`.

Visual Studio's equivalent is Project Properties → **Signing** → *Create Test Certificate*,
which produces an RSA 1024 key rather than 2048.

> The key is a throwaway per-developer identity: self-signed, untrusted by Windows, valid for
> a year. It is excluded from source control on purpose. Shipping one key with a template would
> give every user the same publisher identity — and ClickOnce keys publisher trust to the
> certificate, so anyone holding it could sign an update that a machine which already trusts
> that publisher would accept without prompting. For real distribution, use a code-signing
> certificate from a certificate authority; a self-signed key always shows "Unknown Publisher"
> on first install.

#### Then build

VS 2019's MSBuild builds everything, including the SDK-style libraries: it uses the .NET SDK
bundled with Visual Studio, not the .NET 10 CLI SDK, so ordinary `ProjectReference` entries
work and no `global.json` is needed.

```powershell
& "C:\Program Files (x86)\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe" `
  DataverseExcelAddIn.sln /restore /t:Build /p:Configuration=Debug /m
```

> **Do not run `dotnet build` on `DataverseExcelAddIn.sln`.** The CLI has no OfficeTools
> targets, so it fails on `DataverseAddIn.Excel` — and worse, it writes a `project.assets.json` into
> `src/DataverseAddIn.Excel/obj`, after which VS 2019 fails with *"Your project file doesn't list 'win'
> as a RuntimeIdentifier"*. The fix is to delete `src/DataverseAddIn.Excel/obj`; adding
> `RuntimeIdentifiers`, which the error suggests, does not help.
>
> `dotnet test` against the test project directly is fine — it never touches the add-in.
>
> Use `pwsh tools/build-and-test.ps1` rather than remembering this. It builds every project the
> CLI can build, runs both test suites and the connection-dialog check, and cleans the VSTO
> `obj` folder afterwards. The VS Code C# language server restores the whole solution in the
> background, so those artifacts come back on their own — cleaning once is not enough.
> `.github/workflows/build.yml` runs the same script on `windows-latest` with
> `-FailOnVstoArtifacts`, where nothing should be restoring that project at all.

Three things the VSTO template does not do for you, already applied to `DataverseAddIn.Excel`:

- `DataverseRibbon.xml` must be an **EmbeddedResource**, and the name passed to
  `GetResourceText` must match `<RootNamespace>.DataverseRibbon.xml` exactly.
- A reference to **System.Configuration** is required to read `appSettings`.
- NuGet references of the referenced SDK-style projects do **not** flow to a legacy project,
  so `Microsoft.Xrm.Sdk`, `Microsoft.Crm.Sdk.Proxy` and
  `Microsoft.PowerPlatform.Dataverse.Client` are referenced by `HintPath`, and a copy target
  brings `ServiceClient`'s ~130-assembly closure into the output. `ProjectReference` alone
  copies only 37 DLLs, which fails at run time.

Two VSTO traps worth knowing:

- The generated `ThisAddIn.Designer.cs` uses the project's `RootNamespace` (here
  `DataverseAddIn.Excel`). C# namespaces are case-sensitive, so a file declaring a
  differently-cased namespace silently creates a *different* class and the partial merge
  fails with `CS0115: no suitable method found to override`.
- **The ribbon's `OnLoad` runs before `ThisAddIn_Startup`.** Anything the ribbon touches must
  be created lazily, not assigned in Startup, or it is null when the ribbon loads.

### Know what you are loading into Excel

Dependency closure per library, from the build output:

| Library | DLLs in output |
| --- | --- |
| `DataverseAddIn.Discovery` | 8 |
| `DataverseAddIn.Ingestion` | 14 |
| `DataverseAddIn.Connections` | 135 |
| `DataverseAddIn.WinForms` | 136 |

`ServiceClient` brings 120+ assemblies. That is the whole reason discovery and ingestion are
separate assemblies — a component that only needs Global Discovery plus Web API calls loads 8
DLLs instead of 136. In a VSTO add-in sharing Excel's AppDomain with every other add-in, each
of those is a potential binding-redirect conflict. Prefer `DataverseWebApiClient` over
`ServiceClient` wherever the SDK's message types are not actually required.

Note also that Office here is **x64** on an ARM64 OS, so the add-in runs under emulation.
Keep `PlatformTarget` as `AnyCPU`.

### Running the add-in

1. Install the **Office/SharePoint development** workload in the Visual Studio Installer.
2. Put your own `ClientId` values in `src/DataverseAddIn.Excel/app.config`; it is deployed as
   `DataverseAddIn.Excel.dll.config`. A VSTO add-in does not read `App.config` from anywhere else.
3. Open `DataverseExcelAddIn.sln` in Visual Studio 2019, set `DataverseAddIn.Excel` as the
   startup project, and press **F5**. Excel launches with the add-in loaded and a **Dataverse**
   tab containing **Connections** and **Who Am I**.

If you rename the add-in project in your fork, delete the stale registration afterwards or
Excel will keep loading the old one:

```powershell
Remove-Item 'HKCU:\Software\Microsoft\Office\Excel\Addins\<old name>' -Recurse
```

### How the button state works

`Who Am I` uses a `getEnabled` callback rather than a stored state:

```csharp
public bool GetWhoAmIEnabled(Office.IRibbonControl control) =>
    ThisAddIn.Connections != null && ThisAddIn.Connections.IsConnected;
```

Office only calls it when the control is invalidated, so `DataverseConnectionManager`
raises `ConnectionChanged` and the ribbon responds with
`_ribbon.InvalidateControl("btnWhoAmI")`. Connect, disconnect, and deleting the active
connection all flow through that one event.

### VSTO gotchas

Reference `DataverseAddIn.Discovery` from the VSTO project (a legacy-style `.csproj` can
`ProjectReference` an SDK-style net462 library), then create **one** authenticator for the
lifetime of the add-in:

```csharp
public partial class ThisAddIn
{
    internal static DataverseAuthenticator Authenticator { get; private set; }

    private void ThisAddIn_Startup(object sender, EventArgs e)
    {
        Authenticator = new DataverseAuthenticator(new DataverseAuthOptions
        {
            ClientId = "<your client id>",
            TenantId = "organizations",
            Cloud    = DataverseCloud.Commercial,
            // Parent the sign-in window to Excel so it can never appear behind it.
            ParentWindowHandleProvider = () => new IntPtr(Application.Hwnd)
        });
    }
}
```

Ribbon button handler — note `async void` is correct here (it is an event handler) and
nothing ever blocks:

```csharp
public async void OnChooseEnvironment(Office.IRibbonControl control)
{
    try
    {
        using (var discovery = new GlobalDiscoveryClient(ThisAddIn.Authenticator))
        {
            var environments = await discovery.GetInstancesAsync(filter: "State eq 0");
            // bind environments to your task pane / dialog
        }
    }
    catch (Exception ex)
    {
        MessageBox.Show(ex.Message, "Sign-in failed");
    }
}
```

### VSTO gotchas

- **Never** call `.Result`, `.Wait()`, or `GetAwaiter().GetResult()` on these tasks from
  Excel's UI thread. VSTO runs on an STA with a message-pumping synchronization context,
  and blocking on an MSAL task there deadlocks Excel. The library uses
  `ConfigureAwait(false)` throughout so `await` is always safe.
- Keep `PlatformTarget` as `AnyCPU`, `x86`, or `x64` — never blank. WebView2 (if you ever
  opt into the embedded browser) cannot resolve its native loader otherwise.
- The default is the **system browser**, not the embedded one. On .NET Framework the
  embedded web view is WebView1 (Internet Explorer), which breaks FIDO keys and Windows
  Hello and trips several Conditional Access policies. Only set
  `UseEmbeddedWebView = true` if a system-browser popup is unacceptable, and add the
  `Microsoft.Identity.Client.Desktop` package plus `ms-appx-web://…` handling if you do.
- Token cache is a DPAPI-encrypted file under `%LOCALAPPDATA%\DataverseDiscovery\`,
  partitioned per authority host and client ID, so users sign in once and the commercial
  and government caches never overwrite each other. Override with
  `DataverseAuthOptions.TokenCacheFilePath` — but keep the partitioning if you do.
- VSTO add-ins do not honour `App.config` — read settings from the add-in's own
  `.dll.config` or hard-code/bake them at build time.

## Behaviour worth knowing

- **Tokens are per-resource *and* per-authority.** The discovery service and each
  environment are separate audiences. `DataverseAuthenticator` handles this: the first call
  on an authority prompts, every later resource on that authority is acquired silently.
- **Results are single-tenant.** Global Discovery returns only environments in the tenant
  the token was issued for. To cover guest access across tenants, enumerate the user's
  tenants (ARM `/tenants` or Microsoft Graph) and repeat discovery with `TenantId` set to
  each one. That enumeration is per identity cloud — commercial guest tenants and
  government tenants are never returned together.
- **Empty list is a valid response.** GDS deliberately omits environments where the
  account is disabled, filtered out by an environment security group, or accessed only
  through delegated administration.
- **`$filter` string comparisons are case sensitive**, unlike the Dataverse Web API.
- Use `FriendlyName` for UI and `ApiUrl` for connecting. Server allocation changes over
  time, so don't cache `ApiUrl` indefinitely.

## Sovereign clouds

Set `DataverseAuthOptions.Cloud`; the discovery endpoint and the Entra login host are
both switched for you.

| Cloud | Discovery endpoint | Login host | Identity directory |
| --- | --- | --- | --- |
| `Commercial` | `globaldisco.crm.dynamics.com` | `login.microsoftonline.com` | Public Entra ID |
| `UsGovernmentCommunity` (GCC) | `globaldisco.crm9.dynamics.com` | `login.microsoftonline.com` | Public Entra ID |
| `UsGovernmentHigh` | `globaldisco.crm.microsoftdynamics.us` | `login.microsoftonline.us` | Entra Government |
| `UsDepartmentOfDefense` | `globaldisco.crm.appsplatform.us` | `login.microsoftonline.us` | Entra Government |
| `China` | `globaldisco.crm.dynamics.cn` | `login.chinacloudapi.cn` | 21Vianet |

`cloud.UsesGovernmentIdentity()` and `cloudA.SharesIdentityWith(cloudB)` let you decide at
runtime whether a second app registration and a second sign-in are required.

### Government-cloud gotchas

- **GCC is not a sovereign identity cloud.** It is public Entra ID with separate Dataverse
  endpoints. Reuse the commercial app registration and the commercial sign-in; only the
  discovery endpoint and the token audience change. Sending GCC users to
  `login.microsoftonline.us` is the most common mistake here.
- **GCC High and DoD app registrations must be created in Entra Government**
  (`portal.azure.us`). "Multitenant" does not bridge the two identity clouds.
- **Do not assume a user has an identity in both.** Most have one. Treat a failure from
  the other cloud as expected, not exceptional — that is why `MultiCloudDiscoveryClient`
  collects failures instead of throwing.
- **Region label vs cloud.** GCC hosts look like `contoso.crm9.dynamics.com` — same
  `dynamics.com` suffix as commercial, different region label. `TryGetCloudFromUrl`
  accounts for this; naive suffix matching does not.
- Government tenants often proxy or restrict outbound traffic. If sign-in stalls, confirm
  that both the login host and the discovery host for that cloud are allowed.

[gds]: https://learn.microsoft.com/en-us/power-apps/developer/data-platform/discovery-service#global-discovery-service
[oauth]: https://learn.microsoft.com/en-us/power-apps/developer/data-platform/authenticate-oauth

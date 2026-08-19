# Architecture

Five projects, and the boundaries between them are the design. Each one exists because
something concrete goes wrong without it.

```
DataverseAddIn.Discovery ──┐
   MSAL, Global Discovery  │
   Web API. 8 DLLs.        │
                           ├──> DataverseAddIn.Connections ──> DataverseAddIn.ExcelHost
DataverseAddIn.Ingestion ──┘       ServiceClient, credentials,      ribbon, ThisAddIn
   Microsoft.Xrm.Sdk only.         saved connections. 135 DLLs.     (the only Excel-aware
   No auth, no ServiceClient.                │                       project)
                                             v
                                    DataverseAddIn.WinForms
                                      dialogs, host-agnostic
```

Dependencies point one way. Nothing under `src/` except `DataverseAddIn.ExcelHost` knows Excel
exists, and nothing except `DataverseAddIn.WinForms` knows a UI exists.

The host project is `ExcelHost` rather than `Excel` on purpose: a namespace segment shadows a
using-alias of the same name, which breaks `using Excel = Microsoft.Office.Interop.Excel;` and
every pasted Office sample with it. See [vsto.md](vsto.md#why-the-project-is-called-excelhost-and-not-excel).

## Why the split

**Assembly count is a correctness problem, not an aesthetic one.** A VSTO add-in shares one
AppDomain with Excel and every other installed add-in, so each assembly loaded is a potential
binding-redirect conflict. The measured closures:

| Library | DLLs in output |
| --- | --- |
| `DataverseAddIn.Discovery` | 8 |
| `DataverseAddIn.Ingestion` | 14 |
| `DataverseAddIn.Connections` | 135 |
| `DataverseAddIn.WinForms` | 136 |

`ServiceClient` alone brings 120+. A component that only needs discovery and Web API calls
loads 8 instead of 136. Prefer `DataverseWebApiClient` over `ServiceClient` wherever the
SDK's strongly-typed messages are not actually needed.

**Testability drove the ingestion split.** The engine takes a `Func<IOrganizationService>`
and an integer degree of parallelism — no `ServiceClient`, no auth. That is why chunking,
strategy selection, error-index mapping and retry are covered by tests that run offline in
under a second. Coupled to `ServiceClient`, which needs a live connection to construct, none
of it would be reachable. See [0004](../decisions/0004-ingestion-engine.md).

## The authentication abstraction

Two interfaces, because one is not enough.

```csharp
// DataverseAddIn.Discovery — "give me a token"
public interface IDataverseTokenSource
{
    DataverseCloud Cloud { get; }
    bool IsInteractive { get; }
    bool SupportsGlobalDiscovery { get; }
    Task<string> GetTokenAsync(string resourceUrl, CancellationToken ct = default);
    Task SignOutAsync();
}

// DataverseAddIn.Connections — "connect me"
public interface IDataverseCredential
{
    DataverseAuthKind Kind { get; }
    DataverseCloud Cloud { get; }
    IDataverseTokenSource TokenSource { get; }   // null when the kind has no bearer token
    Task<ServiceClient> CreateClientAsync(DataverseEnvironmentReference environment, ...);
}
```

A token source is not sufficient on its own: on-premises Active Directory and IFD reach
Dataverse through a `ServiceClient` connection string and have **no bearer token at all**.
That is what the nullable `TokenSource` models, and it is the single place those kinds are
special-cased. `DataverseConnectionManager.GetTokenSource` turns the null into a
`NotSupportedException` that names the kind, rather than a `NullReferenceException` three
frames later.

Everything that talks to Dataverse over HTTP — `GlobalDiscoveryClient`,
`MultiCloudDiscoveryClient`, `DataverseWebApiClient`, `DataverseServiceClientFactory` —
depends on `IDataverseTokenSource`, never on a concrete authenticator.

### Two flags that do real work

`IsInteractive` exists because `ServiceClient` calls its token provider **synchronously**.
If that call has to prompt while running on Excel's STA thread, Excel deadlocks.
`DataverseServiceClientFactory` pre-acquires the token on the async path first, but only
when the source can actually show UI.

`SupportsGlobalDiscovery` is false for service principals and will be false for on-premises.
Global Discovery enumerates environments *for a signed-in user*; an application user has none
to enumerate. `GlobalDiscoveryClient` rejects such a source in its constructor rather than
failing later with an opaque 401, and the UI falls back to asking for an environment URL.

### Identity: `CredentialSpec`

`(Cloud, Kind, ClientId, TenantId, Principal)` with case-insensitive value equality. It is
the credential cache key **and** the token-cache file key.

Cloud alone is not an identity. A service principal and a signed-in user can target the same
cloud, as can two app registrations or two users. Keying on cloud alone means two credentials
silently share one MSAL cache and overwrite each other. `Principal` is deliberately loose —
the user name for interactive, the thumbprint for a certificate, the secret reference for a
client secret — because its only job is separating identities inside one app registration.

The token cache file follows the same key:
`{authorityHost}.{kind}.{sha256 of clientId|tenantId|principal}.msalcache`. Hashed so the
name stays short and carries no user name.

### The descriptor table

`AuthKindDescriptor` carries display name, description, warning, required and optional
fields, and the two behaviour flags. The connection dialog renders itself from it — there is
**no `switch` on authentication kind anywhere in the UI**.

`DataverseAuthKind` declares every kind, including ones with no implementation, because it is
a persisted contract. `AuthKindDescriptor` registers **only** kinds that have a working
credential, so `AuthKindDescriptor.Supported` is safe to bind straight to a picker. Descriptor
exists if and only if credential exists — a test enforces it.

## Adding an authentication kind

Four steps, none of which touch a consumer:

1. Implement `IDataverseTokenSource` in `DataverseAddIn.Discovery` (or skip it, returning
   null from `TokenSource`, if the kind has no bearer token).
2. Implement `IDataverseCredential` in `DataverseAddIn.Connections`.
3. Register an `AuthKindDescriptor` with the fields the kind needs.
4. Add a case to `CredentialFactory.Create`.

The dialog, the connection manager, the list view and the discovery gating all pick it up
from the descriptor. `ClientSecretCredential` was added exactly this way and changed no
existing consumer.

Kinds still unimplemented: `DeviceCode`, `UsernamePassword`, `ExternalToken`,
`ConnectionString`, `WindowsIntegrated`, `Ifd`.

## Secrets

`connections.json` lives in the roaming profile as plain JSON and holds **only a reference**.
The secret itself is a DPAPI blob under the *local* profile, scoped to the current Windows
user, so a copied file decrypts to nothing.

Deliberately not a shared-passphrase scheme: with the passphrase compiled into an
open-source assembly, a copied secrets file is a decrypt. A failed decrypt returns null so
the user is re-prompted rather than shown a crypto exception.

Switching a connection away from a secret-bearing kind **deletes** the stored secret rather
than orphaning it.

## Testing

173 tests, all offline — no network, no credentials, no Office.

```powershell
pwsh tools/build-and-test.ps1
```

Two things are deliberately **not** unit tested, and knowing why matters:

**`DataverseConnectionManager.ConnectAsync`** returns a concrete `ServiceClient`, which needs
a live environment. `ServiceClient` is not sealed, but every member the code uses is
non-virtual and there is no constructor that wraps an `IOrganizationService`, so a fake buys
nothing. Instead the *decision* was extracted — `ConnectionProfile.AdoptOrganizationName` is
pure and fully tested — leaving `ConnectAsync` as orchestration with no branching. Revisit
if it ever grows retry or reconnect logic.

**The connection dialog** is WinForms, and its worst failure mode only appears when the form
is off screen. `tools/verify-connection-dialog.ps1` covers it and asserts the after-close
case explicitly. Run it after changing the dialog or adding a descriptor; the CI script runs
it automatically.

### Mutation testing is the convention here

A green suite that has never been seen red proves nothing. Every significant guard in this
repo was verified by deliberately breaking it and confirming which tests failed — which is
how the `Equals`/`GetHashCode` gap was found: a broken `Equals` was invisible to the
dictionary tests because `GetHashCode` still separated the values.

Two rules learned the hard way:

- Check *which* tests go red, not just that some do. A mutation failing one test where you
  expected three is telling you two are weaker than they look.
- Confirm the mutation is actually **executed**. A mutation placed after a throwing call is
  unreachable, and proves nothing about the tests.

## Further reading

- [Authentication](authentication.md) — app registrations, clouds, scopes, secrets
- [VSTO](vsto.md) — building, signing, ribbon, Office-specific traps
- [Ingestion](ingestion.md) — throughput model and batching
- [decisions/](../decisions) — the reasoning, dated, with what was rejected and why

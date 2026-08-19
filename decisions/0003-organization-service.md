# 0003 — Use ServiceClient for the Organization Service, in a separate assembly

- Status: accepted
- Date: 2026-08-18
- Extends [0001](0001-discovery-approach.md) and [0002](0002-multi-cloud-model.md)

## Context

The add-in will use the C# SDK, so it needs `IOrganizationService` — SDK messages,
FetchXML, `ExecuteMultiple`, metadata, early-bound types. Discovery alone is not enough.

Decision 0001 deliberately avoided `Microsoft.PowerPlatform.Dataverse.Client` because its
dependency graph (`Microsoft.Extensions.*`, `Newtonsoft.Json`, `System.Text.Json`, WCF) is
a binding-redirect hazard inside Excel's shared AppDomain. That reasoning does not survive
a requirement for `IOrganizationService`: there is no lightweight alternative.

## Decision

Use `ServiceClient`, via the constructor that delegates authentication to the caller:

```csharp
ServiceClient(Uri instanceUrl, Func<string,Task<string>> tokenProviderFunction,
              bool useUniqueInstance = true, ILogger logger = null)
```

Place it in a **separate assembly**, `DataverseAddIn.Connections`, rather than in
`DataverseAddIn.Discovery`.

Supporting choices:

- `DataverseServiceClientFactory.CreateAsync` awaits a token **before** constructing
  `ServiceClient`, and constructs it inside `Task.Run`.
- `useUniqueInstance: true`.
- The factory rejects an environment whose cloud does not match the authenticator's.
- The Web API DTO was renamed `WhoAmIResponse` → `WhoAmIResult`.

## Rationale

- **Caller-managed auth keeps one sign-in.** The alternative overloads take
  `ClientCredentials` or username/password, which fail under MFA and Conditional Access —
  non-negotiable in the GCC High tenant this targets. Delegating to the existing
  `DataverseAuthenticator` means discovery and every environment share one MSAL cache, so
  the user signs in once and additional orgs resolve silently. Verified: connecting to a
  second environment produced no prompt.
- **A separate assembly preserves 0001's benefit.** If a caller only needs discovery plus
  Web API calls, it references `DataverseAddIn.Discovery` and the SDK graph is never loaded into
  Excel. One extra project file is cheap insurance against VSTO assembly conflicts.
- **The deadlock is real, not theoretical.** `ServiceClient` invokes the token provider
  synchronously. An interactive MSAL prompt triggered from that call on Excel's STA UI
  thread deadlocks the process. Pre-warming the token on the async path guarantees the
  provider only hits the cache; `Task.Run` keeps the remaining sync-over-async off the UI
  thread.
- **`useUniqueInstance: true`** because `ServiceClient` otherwise reuses cached connections.
  An add-in whose whole purpose is switching environments could silently keep talking to
  the previous one.
- **The rename avoids an ambiguity every consumer would hit.** `Microsoft.Crm.Sdk.Messages`
  also defines `WhoAmIResponse`, and any file using both assemblies fails to compile with
  `CS0104`. Caught at build time.

## Consequences

- Two ways to reach an environment now exist: `DataverseWebApiClient` (no SDK) and
  `ServiceClient` (full SDK). Both accept the same `DataverseEnvironmentReference`.
- `DataverseEnvironmentReference` lives in `DataverseAddIn.Discovery` so the "user typed a URL"
  path needs no SDK reference to parse and classify input.
- `ServiceClient` is `IDisposable` and not intended for sharing across threads; callers must
  dispose it and use `Clone()` for parallelism.
- Verified against a live GCC High environment through both entry points: a discovery
  selection, and a bare host string with no scheme.

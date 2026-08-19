# 0001 — Call Global Discovery over REST with MSAL rather than ServiceClient

- Status: accepted
- Date: 2026-08-18
- Context: sample library that an Excel VSTO add-in (.NET Framework 4.6.2) will consume

## Context

Microsoft documents three ways to enumerate a user's Dataverse environments:

1. `HttpClient` against the Global Discovery Service OData endpoint, with a token from MSAL.
2. `ServiceClient.DiscoverOnlineOrganizationsAsync` (`Microsoft.PowerPlatform.Dataverse.Client`).
3. `CrmServiceClient.DiscoverGlobalOrganizations` (`Microsoft.Xrm.Tooling.Connector`, legacy).

The consumer is a VSTO add-in, which is unusual in two ways: it shares a single AppDomain
with Excel and every other installed add-in, and it runs on a message-pumping STA UI thread.

## Decision

Use option 1 — MSAL.NET (`Microsoft.Identity.Client`) for the token, `HttpClient` for the
call, and the in-box `DataContractJsonSerializer` for JSON.

## Rationale

- **Dependency surface.** `Microsoft.PowerPlatform.Dataverse.Client` transitively pulls in
  `Microsoft.Extensions.*`, `Newtonsoft.Json`, `System.Text.Json`, and WCF assemblies. In a
  shared Office AppDomain each of those is a version conflict waiting to happen, and VSTO's
  binding-redirect story is poor. Option 1 needs exactly one package.
- **Query support.** `DiscoverOnlineOrganizationsAsync` ignores `$select` and `$filter`
  entirely, and returns `OrganizationDetail` instead of the richer `Instance` shape
  (`EnvironmentId`, `IsUserSysAdmin`, `Purpose`, `Region`, `StatusMessage`).
- **Authentication control.** The convenient `ServiceClient`/`CrmServiceClient` overloads
  take `ClientCredentials` (username + password), which fails under MFA and Conditional
  Access and is explicitly discouraged by Microsoft. Owning the MSAL instance also lets us
  parent the sign-in window to Excel's HWND and control the token cache.
- Option 3 is legacy and adds nothing over option 2 here.

## Consequences

- We hand-roll the `Instance` DTO and the OData request. Small and stable — the entity set
  has one shape and is versioned at `v2.0`.
- `DataContractJsonSerializer` cannot read ISO 8601 into `DateTimeOffset`, so date and Guid
  members are declared as `string` with typed convenience properties beside them.
- Token cache persistence is hand-rolled over DPAPI instead of using
  `Microsoft.Identity.Client.Extensions.Msal`, for the same dependency reason. If the add-in
  ever needs cross-process cache locking, revisit this.
- If the add-in later adopts `ServiceClient` for data operations anyway, the dependency
  argument weakens; at that point switch discovery to the
  `DiscoverOnlineOrganizationsAsync(Func<string, Task<string>>, …)` overload and feed it
  `DataverseAuthenticator.AcquireDiscoveryTokenAsync`.

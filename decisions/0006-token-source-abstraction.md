# 0006 — Depend on an `IDataverseTokenSource` abstraction, not on `DataverseAuthenticator`

- Status: accepted
- Date: 2026-08-19
- Step 1 of a staged move to multiple authentication types

## Context

Every component that talked to Dataverse took a concrete `DataverseAuthenticator`, which is
hard-wired to one authentication type: an interactive MSAL public client acquiring
`<resource>/user_impersonation`. `GlobalDiscoveryClient`, `MultiCloudDiscoveryClient`,
`DataverseWebApiClient`, `DataverseServiceClientFactory` and `DataverseConnectionManager` all
named that class in their signatures.

Adding client secret, certificate, device code, ROPC, connection string or on-premises AD
would have meant editing all five, and every future consumer, once per authentication type.

`MscrmTools.Xrm.Connection` solves the same problem with a single `ConnectionDetail` class
whose `GetCrmServiceClient` is a long `if/else` chain over auth-shaped properties. That works
but leaves every caller coupled to the union of all authentication types, and it routes
everything through a connection string, which discards the shared MSAL token cache.

## Decision

Introduce `IDataverseTokenSource` — `Cloud`, `IsInteractive`, `SupportsGlobalDiscovery`,
`GetTokenAsync(resourceUrl, ct)`, `SignOutAsync()` — and depend on it everywhere.
`DataverseAuthenticator` becomes its first implementation and is otherwise unchanged.

This step is behaviour-preserving. No new authentication type is added here.

## Rationale

- **The interface is the whole extensibility point.** A new authentication type becomes one
  new class; the five consumers never change again.
- **Tokens, not credentials, are what HTTP callers need.** Returning `string` rather than
  MSAL's `AuthenticationResult` keeps `Microsoft.Identity.Client` out of the contract, so a
  non-MSAL source (Azure CLI, managed identity, a caller-supplied token) can implement it.
- **`SupportsGlobalDiscovery` makes the awkward cases honest.** On-premises deployments have
  no Global Discovery Service, and service principals usually cannot call it. `GlobalDiscoveryClient`
  now rejects such a source in its constructor rather than failing with an opaque 401, and UI
  can fall back to asking for an environment URL.
- **`IsInteractive` names the reason for an existing workaround.** `DataverseServiceClientFactory`
  pre-acquires a token on the async path because `ServiceClient` calls the token provider
  synchronously, which would deadlock Excel's STA thread. That only matters when acquisition
  can show UI, so the pre-warm is now conditional on the flag instead of unconditional.

## Consequences

- `DataverseConnectionManager.GetAuthenticator(cloud)` is renamed to `GetTokenSource(cloud)`
  and returns the interface. Its cache stays keyed by cloud alone, which is **wrong once a
  second authentication type exists** — two credentials for the same cloud will collide. Fixing
  the key to `(Cloud, Kind, ClientId, TenantId, principal)` is part of the next step, along
  with the same fix in `DataverseAuthOptions.ResolveTokenCacheFilePath()`.
- `DataverseAuthenticator.AcquireTokenAsync` / `AcquireDiscoveryTokenAsync` /
  `AcquireEnvironmentTokenAsync` are kept. They still return `AuthenticationResult` for callers
  that want the claims, and the README references them.
- Two things deliberately not solved here, because they are not token concerns:
  - **Scope varies by client type.** Public clients use `<resource>/user_impersonation`,
    confidential clients use `<resource>/.default`. `BuildScope` still hardcodes the former;
    scope must become a property of the credential.
  - **Some authentication types have no token at all.** On-premises AD and IFD reach Dataverse
    only through a `ServiceClient` connection string. They will be modelled by a separate
    `IDataverseCredential` that returns a null token source.

## Step 2 — `IDataverseCredential` and a descriptor table

- Date: 2026-08-19

`IDataverseTokenSource` answers "give me a token". It cannot answer "connect me", because AD
and IFD have no token. `IDataverseCredential` (`Kind`, `Cloud`, `TokenSource`,
`CreateClientAsync`) is the second half: one authentication kind, bound to one cloud, able to
produce a connected `ServiceClient`. `TokenSource` is nullable, and that null is the single
place the token-less kinds are modelled — `DataverseConnectionManager.GetTokenSource` turns it
into a `NotSupportedException` naming the kind, instead of a `NullReferenceException`.

`InteractiveCredential` is the only implementation. It wraps `DataverseAuthenticator` and
delegates to `DataverseServiceClientFactory`, so the STA pre-warm stays in one place.

`DataverseAuthKind` declares the full vocabulary now, including kinds with no implementation.
The enum is a persisted contract (profiles will store it by name), so fixing the names once is
cheaper than adding them one per step. `AuthKindDescriptor`, by contrast, registers **only**
kinds that have a credential, so `AuthKindDescriptor.Supported` can be bound straight to a
picker without offering something that throws. Descriptor exists if and only if credential
exists.

`DataverseConnectionManager` now caches `IDataverseCredential` per cloud and `ConnectAsync`
goes through it. The existing `Func<DataverseCloud, DataverseAuthOptions>` constructor is kept
and wraps its result in an `InteractiveCredential`, so hosts and samples are unchanged. The
credential-based entry point is the static `WithCredentials(...)` rather than a second
constructor: both delegates have the same arity, and a lambda returning `null` would be an
ambiguous call.

Still outstanding, unchanged from step 1: the `.default` scope for confidential clients, the
cloud-only cache key, `AppTokenCache` vs `UserTokenCache`, and secret storage. Profiles still
carry no `AuthKind`, so every connection is interactive — that is step 3.

## Step 3 — identity, secrets, and the cache key

- Date: 2026-08-19

Three things had to change together, because each is unsafe without the others.

**`CredentialSpec` replaces `DataverseCloud` as the credential key.** Cloud alone was never a
sufficient identity; it only looked sufficient while interactive sign-in was the sole kind. A
spec is `(Cloud, Kind, ClientId, TenantId, Principal)` with case-insensitive value equality, so
a service principal and a signed-in user on one cloud, or two app registrations, or two users,
each get their own credential instance. `Principal` is deliberately loose — it is the user name
for interactive and ROPC, the thumbprint for a certificate, and the secret reference for a
client secret — because its only job is to separate identities within one app registration.

**The MSAL token cache file name now derives from the same identity.** It was
`{authorityHost}.{clientId}.msalcache`, which two credentials could collide on. It is now
`{authorityHost}.{kind}.{sha256-of-clientId-tenantId-principal}.msalcache`. Hashing keeps the
name short and keeps the user name out of a file name. **This invalidates existing caches**:
every user signs in once more after upgrading, and the old files are orphaned rather than
migrated. Acceptable once, before other kinds ship; unacceptable later.

**Secrets go in `ISecretStore`, never in `connections.json`.** That file is plain JSON in the
roaming profile, so `ConnectionProfile` gains a `SecretRef` and nothing else. `DpapiSecretStore`
writes DPAPI-encrypted files under the *local* profile, scoped to `CurrentUser`, with a
`^[A-Za-z0-9_-]{1,64}$` guard on the reference because it becomes a file name. A decrypt failure
returns null rather than throwing, so a file copied from another machine causes a re-prompt
instead of a crash. `DataverseConnectionManager.Delete` now deletes the secret with the profile.

Explicitly **not** the shared-passphrase `CryptoManager` approach used by some Dataverse
tooling: with the passphrase compiled into an open-source assembly, a copied secrets file is a
decrypt. DPAPI ties the ciphertext to the Windows account, so secrets do not survive a move to
another machine — the right trade-off for a desktop add-in, and the reason the store lives under
`LocalApplicationData` while connections roam.

Verified out of band: secret round-trip, no plaintext on disk, null for unknown and deleted
references, `ArgumentException` on a traversal reference, `CredentialSpec` collapsing to two
dictionary entries across three case-varying specs, and four distinct cache file names for four
identities differing by one field each.

Per-connection `ClientId` and `TenantId` on the profile now override the host defaults, which is
what lets one add-in hold a normal user connection and a service-principal connection at once.

Remaining for step 4: the `.default` scope for confidential clients, and a `DpapiTokenCache`
overload for MSAL's `AppTokenCache` — confidential clients do not use `UserTokenCache`, so
without it a secret or certificate credential re-acquires a token on every call.

## Step 4 — the second authentication kind

- Date: 2026-08-19

`ClientSecretCredential` is the first kind added since the abstraction landed, and it cost one
new token source, one new credential, and a descriptor entry. No consumer changed, which was
the point of steps 1 and 2.

**Confidential clients use `<resource>/.default`, not `/user_impersonation`.** Microsoft's
guidance is explicit and the two are not interchangeable: sending the delegated scope on a
client-credentials request fails at Entra ID. `DataverseScope.Application` sits next to
`DataverseScope.Delegated`, and a test asserts the two are never equal.

**Tokens go through the same path as an interactive connection**, not the SDK's
`AuthType=ClientSecret` connection string. One token then serves `ServiceClient` and the direct
Web API clients, and the cache is shared. The connection-string route would have been fewer
lines but a second code path.

**`SupportsGlobalDiscovery` is false.** Global Discovery enumerates environments for a
signed-in user; a service principal has none to enumerate. This is the first credential where
the flag does real work — the descriptor carries a warning, and the UI has to fall back to
asking for an environment URL.

**Multi-tenant authorities are rejected at construction.** `DataverseAuthOptions.TenantId`
defaults to `organizations`, which is right for a public client and invalid for client
credentials, so the default configuration would otherwise fail at Entra ID with an opaque
error. `common` and `consumers` are refused for the same reason.

The `AppTokenCache` concern from step 3 needed no new API: `DpapiTokenCache.Attach` already
takes an `ITokenCache`, and `IConfidentialClientApplication.AppTokenCache` is one. What
mattered was attaching to the right cache — `UserTokenCache` stays empty for a confidential
client, so attaching there would silently re-acquire on every call.

`CredentialFactory` now owns the kind-to-implementation mapping, replacing the manager's
private interactive-only factory. `DataverseConnectionManager.WithCredentials` remains for
kinds the factory does not implement.

Still unimplemented: `DeviceCode`, `Certificate`, `UsernamePassword`, `ExternalToken`,
`ConnectionString`, `WindowsIntegrated`, `Ifd`. Nothing can select `ClientSecret` from the UI
yet — that is step 5.

## Step 5 — a descriptor-driven connection dialog

- Date: 2026-08-19

`ConnectionDetailsForm` now renders itself from `AuthKindDescriptor`: a combo bound to
`Supported`, a description and warning line, and one label/editor row per `AuthField` in
`RequiredFields | OptionalFields`. There is no `switch` on the kind anywhere in the UI, so the
next credential appears in the dialog by registering a descriptor and nothing else.

`ConnectionAuthentication` carries the user's choice from dialog to manager. It holds the
secret in memory only; `DataverseConnectionManager.ApplyAuthentication` writes it to
`ISecretStore` and puts the reference on the profile. Two behaviours there are worth naming:

- A blank secret on edit means "keep the saved one", so an edit does not force the user to
  retype a secret they cannot read back.
- Switching a connection away from `ClientSecret` **deletes** the stored secret rather than
  orphaning it. Leaving it behind would be an avoidable secret at rest with nothing to read it.

### The bug that unit tests could not have found

`ValueOf` originally decided whether a field applied by reading `TextBox.Visible`.
`Control.Visible` is false whenever the control is not actually on screen — **including after
`ShowDialog` returns**, which is exactly when `ConnectionManagerForm` reads the result. Every
client-secret connection would have been saved with no client ID, no tenant and no secret, and
would have failed at connect time with a confusing error.

`AuthFieldRow.IsApplicable` now tracks intent, and `Control.Visible` is only an output.

The bug was found by constructing the dialog off-screen and inspecting it, not by any test.
`tools/verify-connection-dialog.ps1` keeps that check runnable, and asserts the after-close
case explicitly. Run it after changing the dialog or adding a descriptor. The general rule: a
WinForms property that reflects *runtime state* must never be used to store *intent*.

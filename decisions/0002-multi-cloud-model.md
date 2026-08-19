# 0002 — Model the cloud as an authenticator-scoped value, not a per-call parameter

- Status: accepted
- Date: 2026-08-18
- Supersedes nothing; extends [0001](0001-discovery-approach.md)

## Context

The add-in must list environments from Commercial, GCC, and GCC High. Microsoft's
[Dynamics 365 US Government][gov] documentation is explicit about the split:

> GCC High … enables and requires the customer to use Microsoft Entra Government for
> customer identities, **in contrast to GCC which uses Public Microsoft Entra ID**.

So the five clouds fall into three identity groups:

| Identity authority | Clouds |
| --- | --- |
| `login.microsoftonline.com` | Commercial, GCC |
| `login.microsoftonline.us` | GCC High, DoD |
| `login.chinacloudapi.cn` | China |

Cloud therefore determines three things at once: the discovery endpoint, the token
audience, and the identity authority — and the authority determines which app
registration and which user account apply.

## Decision

`DataverseCloud` is fixed at `DataverseAuthenticator` construction, not passed per call.
Callers targeting several clouds construct several authenticators and pass them to
`MultiCloudDiscoveryClient`, which fans out and merges.

Supporting choices:

- `DataverseInstance.Cloud` is stamped by `GlobalDiscoveryClient` so a result carries the
  authenticator it belongs to.
- `MultiCloudDiscoveryClient` collects per-cloud failures rather than throwing.
- Clouds are queried sequentially, not in parallel.
- The token cache file is partitioned by authority host and client ID.

## Rationale

- **An authenticator is inseparable from an authority.** MSAL binds authority and client
  ID at `PublicClientApplication` build time. A per-call cloud parameter would have to
  rebuild or pool MSAL instances internally and would silently mismatch cache state.
- **Failures are the normal case, not an error.** Almost no user holds both a commercial
  and an Entra Government identity. Throwing when GCC High discovery fails would make a
  mixed-cloud add-in unusable for every single-cloud user.
- **Sequential avoids a prompt stampede.** The first call on an authority may need
  interactive sign-in. Running clouds in parallel could open two or three sign-in windows
  simultaneously on top of Excel. Sequential ordering also means Commercial warms the
  cache that GCC then hits silently, so the common two-cloud case is still one prompt.
- **Cache partitioning is a correctness fix, not tidiness.** MSAL serializes the entire
  cache per application instance. Two authenticators on different authorities sharing one
  file would overwrite each other's accounts, producing intermittent re-prompts that are
  painful to diagnose.

## Consequences

- Callers hold a collection of authenticators for the add-in's lifetime, and must map an
  environment back to one. `DataverseInstance.Cloud` and
  `DataverseCloudExtensions.TryGetCloudFromUrl` cover the two ways that comes up (live
  result, persisted selection).
- Configuration is per cloud. The console sample resolves `Key.<Cloud>` before falling
  back to `Key`, so Commercial and GCC share a client ID while GCC High supplies its own.
- Adding a cloud is an enum entry plus two switch arms; no call sites change.
- GCC's host (`*.crm9.dynamics.com`) shares the `dynamics.com` suffix with commercial, so
  URL-to-cloud inference must check the region label. Handled in `TryGetCloudFromUrl`.

[gov]: https://learn.microsoft.com/power-platform/admin/microsoft-dynamics-365-government

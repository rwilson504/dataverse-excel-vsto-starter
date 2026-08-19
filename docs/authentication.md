# Authentication

Everything here is delegated OAuth 2.0 against Microsoft Entra ID via MSAL.NET. The moving
parts are which *kind* of credential is used, which *cloud* it belongs to, and which *scope*
that combination requires.

## Supported kinds

| Kind | Client type | Scope | Interactive | Global Discovery |
| --- | --- | --- | --- | --- |
| `Interactive` | public | `<resource>/user_impersonation` | yes | yes |
| `ClientSecret` | confidential | `<resource>/.default` | no | **no** |

Declared but not yet implemented: `DeviceCode`, `Certificate`, `UsernamePassword`,
`ExternalToken`, `ConnectionString`, `WindowsIntegrated`, `Ifd`. See
[architecture](architecture.md#adding-an-authentication-kind) for what each would cost.

### The scope rule

Microsoft's guidance is explicit and the two are **not** interchangeable:

> As demonstrated in the sample code of this article, use a `<environment-url>/user_impersonation`
> scope for a public client. For a confidential client, use a scope of `<environment-url>/.default`.

Sending the delegated scope on a client-credentials request fails at Entra ID.
`DataverseScope.Delegated` and `DataverseScope.Application` encode this, and a test asserts
they never produce the same string.

The resource must have no path and no trailing slash. Dataverse hands back URLs with both,
so `DataverseScope.Resource` reduces to scheme + host first.

### Service principals cannot list environments

`ClientSecret` sets `SupportsGlobalDiscovery = false`. Global Discovery enumerates
environments *for a signed-in user*; an application user has none. The UI surfaces this as a
warning on the kind and falls back to an environment URL.

A client-secret connection also needs a **specific tenant**. `DataverseAuthOptions.TenantId`
defaults to `organizations`, which is correct for a public client and invalid for client
credentials — the flow issues a token for one directory. `ClientSecretTokenSource` rejects
`organizations`, `common` and `consumers` at construction rather than letting it fail at
Entra ID with an opaque error.

The app registration also needs a matching **application user** with a security role in the
environment, created in the Power Platform admin centre.

## App registration

### Do I need my own?

For local experimentation, no. Microsoft publishes a preconfigured sample public client used
throughout the [OAuth docs][oauth]:

| | |
| --- | --- |
| Client ID | `51f81489-12ee-4a9e-aaae-a2591f45987d` |
| Redirect URI | `http://localhost` |
| Authority | `AadAuthorityAudience.AzureAdMultipleOrgs` |

For anything you ship, yes:

> The samples we provide are preconfigured with appropriate registration values so that you
> can run them without generating your own app registration. **When you publish your own
> apps, you must use your own registration values.**

Per cloud:

- **Commercial** — works. Registered multitenant in public Entra ID.
- **GCC** — works. GCC authenticates against public Entra ID too; only the discovery endpoint
  and token audience differ.
- **GCC High / DoD** — the app *does* resolve in Microsoft Entra Government. Verified against
  a real GCC High tenant: the device-code endpoint issued a code for it, while a deliberately
  invalid client ID returned `AADSTS700016` from the same endpoint. App-level blocking is
  unlikely in practice — it is the ID baked into XrmToolBox and much community tooling, so
  blocking it breaks admins' own tools. Expect *conditional* access rather than denial.

  It still isn't a shipping answer, and consent policy is the failure you are more likely to
  hit.

### Registering your own

**How many registrations you need depends on the clouds you target.** Dynamics 365 GCC uses
*public* Entra ID; GCC High and DoD require *Microsoft Entra Government*, a physically
separate directory where a commercial registration is invisible.

| Clouds | Register in | Portal |
| --- | --- | --- |
| Commercial + GCC | Public Microsoft Entra ID | `portal.azure.com` |
| GCC High + DoD | Microsoft Entra Government | `portal.azure.us` |
| China | Entra operated by 21Vianet | `portal.azure.cn` |

The steps are identical in each portal:

1. **App registrations → New registration**
   - Supported account types: **Accounts in any organizational directory** (multitenant) if
     the add-in ships to other tenants, otherwise single tenant.
   - Redirect URI: **Public client/native (mobile & desktop)** → `http://localhost`
2. **Authentication** → *Advanced settings* → **Allow public client flows: Yes**.
3. **API permissions** → *Add a permission* → **Dynamics CRM** → *Delegated permissions* →
   **user_impersonation** → *Add*. This one permission covers both Global Discovery and every
   environment in that cloud.
4. Copy the **Application (client) ID**.

No client secret for the interactive path — a desktop or Office add-in cannot keep one. A
secret is only for the service-principal kind, and it is stored per user via DPAPI, never in
the repository or in `connections.json`.

## What actually fails, and how to read it

| Symptom | Meaning | Fix |
| --- | --- | --- |
| `AADSTS700016` | App not found in that directory | Wrong cloud, or register the app there |
| `AADSTS65001` / `AADSTS90094` | Consent required, user consent disabled | Tenant admin grants admin consent once |
| `AADSTS53003` | Blocked by Conditional Access | Satisfy the policy — device, network, MFA |
| `AADSTS50076` | MFA required | Expected; the system browser handles it |

In a locked-down tenant, **consent policy is the usual blocker, not app blocking**. Many
government tenants disable user consent entirely, so the first sign-in for an app whose
service principal is not already provisioned fails with `AADSTS65001` until an admin consents.

This is also where the browser choice stops being cosmetic. Conditional Access rules that
test device compliance or hybrid join need device authentication, which the .NET Framework
embedded WebView1 cannot do — the same account that signs in fine in the system browser can
fail `AADSTS53003` in an embedded view. That is why `UseEmbeddedWebView` defaults to `false`.

## Probing an environment without credentials

Two unauthenticated checks worth knowing when onboarding a tenant or cloud.

**Which authority, tenant and audience does this environment want?** Call any Web API
endpoint with no token and read the challenge:

```powershell
curl.exe -s -i https://<org>.crm.microsoftdynamics.us/api/data/v9.2/WhoAmI
# WWW-Authenticate: Bearer authorization_uri=https://login.microsoftonline.us/<tenantId>/oauth2/authorize,
#                          resource_id=https://<org>.crm.microsoftdynamics.us/
```

`resource_id` is the exact audience, so the public-client scope is that value plus
`user_impersonation`.

**Does a client ID exist in that directory?** Post to the device-code endpoint:

```powershell
curl.exe -s -X POST https://login.microsoftonline.us/<tenantId>/oauth2/v2.0/devicecode `
  -d "client_id=<clientId>&scope=https%3A%2F%2F<org>.crm.microsoftdynamics.us%2Fuser_impersonation"
```

An unknown app returns `AADSTS700016`. Do **not** use `GET /authorize` for this — Entra
defers app resolution there and serves a sign-in page for a client ID that does not exist, so
it always looks like success.

Success here proves the app *resolves*. It does not prove a consented service principal
exists in the tenant.

## Sovereign clouds

Set `DataverseAuthOptions.Cloud`; the discovery endpoint and the Entra login host both switch.

| Cloud | Discovery endpoint | Login host | Identity directory |
| --- | --- | --- | --- |
| `Commercial` | `globaldisco.crm.dynamics.com` | `login.microsoftonline.com` | Public Entra ID |
| `UsGovernmentCommunity` (GCC) | `globaldisco.crm9.dynamics.com` | `login.microsoftonline.com` | Public Entra ID |
| `UsGovernmentHigh` | `globaldisco.crm.microsoftdynamics.us` | `login.microsoftonline.us` | Entra Government |
| `UsDepartmentOfDefense` | `globaldisco.crm.appsplatform.us` | `login.microsoftonline.us` | Entra Government |
| `China` | `globaldisco.crm.dynamics.cn` | `login.chinacloudapi.cn` | 21Vianet |

`cloud.UsesGovernmentIdentity()` and `cloudA.SharesIdentityWith(cloudB)` decide at runtime
whether a second registration and a second sign-in are required.

### Government-cloud gotchas

- **GCC is not a sovereign identity cloud.** It is public Entra ID with separate Dataverse
  endpoints. Reuse the commercial registration and sign-in; only the discovery endpoint and
  token audience change. Sending GCC users to `login.microsoftonline.us` is the most common
  mistake here.
- **GCC High and DoD registrations must be created in Entra Government** (`portal.azure.us`).
  "Multitenant" does not bridge the two identity clouds.
- **Do not assume a user has an identity in both.** Most have one. Treat a failure from the
  other cloud as expected — which is why `MultiCloudDiscoveryClient` collects failures
  instead of throwing.
- **Region label vs cloud.** GCC hosts look like `contoso.crm9.dynamics.com` — same
  `dynamics.com` suffix as commercial, different region label. `TryGetCloudFromUrl` accounts
  for this; naive suffix matching does not.
- Government tenants often proxy or restrict outbound traffic. If sign-in stalls, confirm
  both the login host and the discovery host for that cloud are allowed.

## Targeting several clouds at once

```csharp
using (var discovery = new MultiCloudDiscoveryClient(tokenSources))
{
    var result = await discovery.GetInstancesAsync(filter: "State eq 0");

    // e.g. no account in Entra Government — warn, don't fail
    foreach (var failure in result.Failures)
        Log(failure.Cloud, failure.Error);

    foreach (var instance in result.Instances)
        Bind(instance);   // instance.Cloud says which credential to use later
}
```

- Discovery is **one call per cloud**, always — even when clouds share identity.
- Sign-in is **one prompt per identity authority**. Commercial and GCC resolve from the same
  cached account; GCC High prompts separately.
- Clouds are queried sequentially, not in parallel: firing several sign-in windows at once
  inside Excel is hostile.

## Token cache

A DPAPI-encrypted file under `%LOCALAPPDATA%\DataverseDiscovery\`, keyed by the full
credential identity so two credentials on one cloud never overwrite each other. Override with
`DataverseAuthOptions.TokenCacheFilePath`, but keep the partitioning if you do.

Confidential clients use MSAL's `AppTokenCache`, not `UserTokenCache`. Attaching to the wrong
one silently re-acquires a token on every call.

## Behaviour worth knowing

- **Tokens are per-resource *and* per-authority.** Discovery and each environment are separate
  audiences. The first call on an authority prompts; later resources on that authority resolve
  silently.
- **Results are single-tenant.** Global Discovery returns only environments in the tenant the
  token was issued for. For guest access, enumerate the user's tenants (ARM `/tenants` or
  Microsoft Graph) and repeat discovery per tenant. That enumeration is per identity cloud.
- **An empty list is a valid response.** GDS omits environments where the account is disabled,
  filtered out by an environment security group, or reached only via delegated administration.
- **`$filter` string comparisons are case sensitive**, unlike the Dataverse Web API.
- Use `FriendlyName` for UI and `ApiUrl` for connecting. Server allocation changes over time,
  so do not cache `ApiUrl` indefinitely.

[oauth]: https://learn.microsoft.com/en-us/power-apps/developer/data-platform/authenticate-oauth

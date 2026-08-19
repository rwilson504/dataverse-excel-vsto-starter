using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace DataverseAddIn.Discovery
{
    /// <summary>
    /// Acquires tokens for a service principal using a client secret. There is no signed-in
    /// user: Dataverse sees an application user, which must exist in the environment and hold a
    /// security role.
    /// </summary>
    public sealed class ClientSecretTokenSource : IDataverseTokenSource
    {
        private readonly IConfidentialClientApplication _app;
        private readonly DataverseAuthOptions _options;

        public ClientSecretTokenSource(DataverseAuthOptions options, string clientSecret)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));

            if (string.IsNullOrWhiteSpace(clientSecret))
                throw new ArgumentException("A client secret is required.", nameof(clientSecret));

            _options.Validate();
            RequireSingleTenant(_options.TenantId);

            _app = ConfidentialClientApplicationBuilder
                .Create(_options.ClientId)
                .WithClientSecret(clientSecret)
                .WithAuthority($"{_options.Cloud.GetAuthorityHost()}/{_options.TenantId}", validateAuthority: false)
                .Build();

            // Confidential clients cache under AppTokenCache; UserTokenCache stays empty, so
            // attaching to the wrong one silently re-acquires a token on every call.
            new DpapiTokenCache(_options.ResolveTokenCacheFilePath()).Attach(_app.AppTokenCache);
        }

        public DataverseCloud Cloud => _options.Cloud;

        public bool IsInteractive => false;

        /// <summary>
        /// Global Discovery enumerates environments for a signed-in user, so a service principal
        /// has nothing to enumerate. Callers must supply an environment URL.
        /// </summary>
        public bool SupportsGlobalDiscovery => false;

        public async Task<string> GetTokenAsync(string resourceUrl, CancellationToken cancellationToken = default)
        {
            var result = await _app
                .AcquireTokenForClient(new[] { DataverseScope.Application(resourceUrl) })
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            return result.AccessToken;
        }

        /// <summary>Nothing to sign out of: there is no user and no refresh token.</summary>
        public Task SignOutAsync() => Task.CompletedTask;

        /// <summary>
        /// The client-credentials flow issues tokens for one directory, so the multi-tenant
        /// aliases a public client can use are rejected outright rather than failing at Entra ID
        /// with an opaque error.
        /// </summary>
        private static void RequireSingleTenant(string tenantId)
        {
            if (!tenantId.Equals("organizations", StringComparison.OrdinalIgnoreCase) &&
                !tenantId.Equals("common", StringComparison.OrdinalIgnoreCase) &&
                !tenantId.Equals("consumers", StringComparison.OrdinalIgnoreCase))
                return;

            throw new InvalidOperationException(
                $"A client secret connection needs a specific tenant ID or domain, not '{tenantId}'. " +
                "The client credentials flow issues a token for one directory.");
        }
    }
}

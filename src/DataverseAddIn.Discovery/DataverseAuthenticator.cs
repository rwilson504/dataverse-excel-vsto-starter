using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace DataverseAddIn.Discovery
{
    /// <summary>
    /// Acquires Microsoft Entra ID access tokens for Dataverse resources by signing a user in
    /// interactively. Create one instance and keep it for the lifetime of the add-in: MSAL
    /// caches tokens in memory per <see cref="IPublicClientApplication"/>.
    /// </summary>
    /// <remarks>
    /// Dataverse tokens are per-resource. The Global Discovery Service and each individual
    /// environment are separate resources, so a token for one is not valid for the other.
    /// After the first interactive sign-in, every additional resource is acquired silently.
    /// </remarks>
    public sealed class DataverseAuthenticator : IDataverseTokenSource
    {
        private readonly IPublicClientApplication _app;
        private readonly DataverseAuthOptions _options;

        public DataverseAuthenticator(DataverseAuthOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _options.Validate();

            var builder = PublicClientApplicationBuilder
                .Create(_options.ClientId)
                .WithAuthority($"{_options.Cloud.GetAuthorityHost()}/{_options.TenantId}", validateAuthority: false)
                .WithRedirectUri(_options.RedirectUri);

            if (_options.ParentWindowHandleProvider != null)
                builder = builder.WithParentActivityOrWindow(_options.ParentWindowHandleProvider);

            _app = builder.Build();

            new DpapiTokenCache(_options.ResolveTokenCacheFilePath()).Attach(_app.UserTokenCache);
        }

        public DataverseCloud Cloud => _options.Cloud;

        public bool IsInteractive => true;

        /// <summary>A signed-in user can enumerate their own environments.</summary>
        public bool SupportsGlobalDiscovery => true;

        public async Task<string> GetTokenAsync(string resourceUrl, CancellationToken cancellationToken = default)
        {
            var result = await AcquireTokenAsync(resourceUrl, cancellationToken).ConfigureAwait(false);
            return result.AccessToken;
        }

        /// <summary>Access token for the Global Discovery Service of the configured cloud.</summary>
        public Task<AuthenticationResult> AcquireDiscoveryTokenAsync(CancellationToken cancellationToken = default) =>
            AcquireTokenAsync(_options.Cloud.GetGlobalDiscoveryUrl(), cancellationToken);

        /// <summary>
        /// Access token for a specific environment. Pass <see cref="DataverseInstance.ApiUrl"/>.
        /// </summary>
        public Task<AuthenticationResult> AcquireEnvironmentTokenAsync(string environmentUrl, CancellationToken cancellationToken = default) =>
            AcquireTokenAsync(environmentUrl, cancellationToken);

        /// <summary>
        /// Silent first, interactive only if MSAL says the user must be prompted.
        /// </summary>
        public async Task<AuthenticationResult> AcquireTokenAsync(string resourceUrl, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(resourceUrl))
                throw new ArgumentException("Resource URL is required.", nameof(resourceUrl));

            var scopes = new[] { DataverseScope.Delegated(resourceUrl) };
            var account = await GetSignedInAccountAsync().ConfigureAwait(false);

            if (account != null)
            {
                try
                {
                    return await _app.AcquireTokenSilent(scopes, account)
                        .ExecuteAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (MsalUiRequiredException)
                {
                    // Expired refresh token, new resource needing consent, or a CA policy: fall through.
                }
            }

            var interactive = _app.AcquireTokenInteractive(scopes)
                .WithUseEmbeddedWebView(_options.UseEmbeddedWebView);

            if (account != null)
                interactive = interactive.WithAccount(account);

            try
            {
                return await InteractiveSignIn
                    .RunAsync(token => interactive.ExecuteAsync(token), _options.InteractiveTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (MsalClientException ex) when (ex.ErrorCode == MsalError.AuthenticationCanceledError)
            {
                // The embedded web view reports a closed window this way; the system browser does not.
                throw new SignInCanceledException("Sign-in was cancelled before it completed.");
            }
        }

        public async Task<IAccount> GetSignedInAccountAsync()
        {
            var accounts = await _app.GetAccountsAsync().ConfigureAwait(false);
            return accounts.FirstOrDefault();
        }

        public async Task SignOutAsync()
        {
            foreach (var account in (await _app.GetAccountsAsync().ConfigureAwait(false)).ToList())
                await _app.RemoveAsync(account).ConfigureAwait(false);
        }
    }
}

using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace DataverseAddIn.Discovery
{
    /// <summary>
    /// Acquires tokens for a service principal using an X.509 certificate from the Windows
    /// certificate store. Like <see cref="ClientSecretTokenSource"/> there is no signed-in user,
    /// but the credential never leaves the store and cannot be read back out of a config file.
    /// </summary>
    public sealed class CertificateTokenSource : IDataverseTokenSource
    {
        private readonly IConfidentialClientApplication _app;
        private readonly DataverseAuthOptions _options;

        public CertificateTokenSource(DataverseAuthOptions options, X509Certificate2 certificate)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));

            if (certificate == null) throw new ArgumentNullException(nameof(certificate));

            if (!certificate.HasPrivateKey)
            {
                throw new InvalidOperationException(
                    $"Certificate {certificate.Thumbprint} has no private key in this store. Import the " +
                    "PFX rather than the .cer, and make sure the key is available to this user.");
            }

            _options.Validate();
            RequireSingleTenant(_options.TenantId);

            _app = ConfidentialClientApplicationBuilder
                .Create(_options.ClientId)
                .WithCertificate(certificate)
                .WithAuthority($"{_options.Cloud.GetAuthorityHost()}/{_options.TenantId}", validateAuthority: false)
                .Build();

            new DpapiTokenCache(_options.ResolveTokenCacheFilePath()).Attach(_app.AppTokenCache);

            Certificate = certificate;
        }

        public X509Certificate2 Certificate { get; }

        public DataverseCloud Cloud => _options.Cloud;

        public bool IsInteractive => false;

        /// <summary>A service principal has no environments of its own to enumerate.</summary>
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
        /// Loads a certificate by thumbprint, trying the user's store before the machine's.
        /// </summary>
        /// <param name="storeLocation">Null searches CurrentUser then LocalMachine.</param>
        public static X509Certificate2 Find(
            string thumbprint,
            StoreLocation? storeLocation = null,
            StoreName storeName = StoreName.My)
        {
            if (string.IsNullOrWhiteSpace(thumbprint))
                throw new ArgumentException("A certificate thumbprint is required.", nameof(thumbprint));

            var normalized = Normalize(thumbprint);

            var locations = storeLocation.HasValue
                ? new[] { storeLocation.Value }
                : new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine };

            foreach (var location in locations)
            {
                var found = FindIn(location, storeName, normalized);
                if (found != null) return found;
            }

            throw new InvalidOperationException(
                $"No certificate with thumbprint {normalized} was found in " +
                string.Join(" or ", Array.ConvertAll(locations, l => $"{l}\\{storeName}")) +
                ". Check the thumbprint, and that the certificate is installed for this account.");
        }

        private static X509Certificate2 FindIn(StoreLocation location, StoreName storeName, string thumbprint)
        {
            var store = new X509Store(storeName, location);

            try
            {
                store.Open(OpenFlags.ReadOnly);

                // validOnly: false — a self-signed or expired-chain certificate is still the one
                // the user configured, and a "not found" error would send them hunting the wrong bug.
                var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);

                return matches.Count > 0 ? matches[0] : null;
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // An inaccessible store (common for LocalMachine without elevation) is not an error.
                return null;
            }
            finally
            {
                store.Close();
            }
        }

        /// <summary>
        /// Thumbprints get pasted from certmgr with spaces, lower case, and an invisible
        /// left-to-right mark on the first character. The store matches none of those.
        /// </summary>
        public static string Normalize(string thumbprint)
        {
            var text = new System.Text.StringBuilder(thumbprint.Length);

            foreach (var character in thumbprint)
            {
                if (Uri.IsHexDigit(character))
                    text.Append(char.ToUpperInvariant(character));
            }

            return text.ToString();
        }

        private static void RequireSingleTenant(string tenantId)
        {
            if (!tenantId.Equals("organizations", StringComparison.OrdinalIgnoreCase) &&
                !tenantId.Equals("common", StringComparison.OrdinalIgnoreCase) &&
                !tenantId.Equals("consumers", StringComparison.OrdinalIgnoreCase))
                return;

            throw new InvalidOperationException(
                $"A certificate connection needs a specific tenant ID or domain, not '{tenantId}'. " +
                "The client credentials flow issues a token for one directory.");
        }
    }
}

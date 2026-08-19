using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DataverseAddIn.Discovery
{
    public sealed class DataverseAuthOptions
    {
        /// <summary>
        /// Application (client) ID of your own Microsoft Entra app registration.
        /// Register it as a public client with "Allow public client flows" enabled.
        /// </summary>
        public string ClientId { get; set; }

        /// <summary>
        /// Tenant to sign in against. Use <c>organizations</c> (the default) for any work or
        /// school account, or a specific tenant ID/domain to pin sign-in to one tenant.
        /// <c>common</c> is deliberately avoided: Dataverse does not accept personal accounts.
        /// </summary>
        public string TenantId { get; set; } = "organizations";

        public DataverseCloud Cloud { get; set; } = DataverseCloud.Commercial;

        /// <summary>
        /// How this credential authenticates. Only used to partition the token cache here; the
        /// flow itself is chosen by the credential implementation.
        /// </summary>
        public DataverseAuthKind AuthKind { get; set; } = DataverseAuthKind.Interactive;

        /// <summary>
        /// What distinguishes this identity from another using the same app registration: a user
        /// name, a certificate thumbprint, or a secret reference. Null when there is only one.
        /// </summary>
        public string Principal { get; set; }

        /// <summary>
        /// Redirect URI. <c>http://localhost</c> works with the system browser and lets MSAL
        /// pick any free port. Register the same value on the app registration.
        /// </summary>
        public string RedirectUri { get; set; } = "http://localhost";

        /// <summary>
        /// Leave <c>false</c> to use the system browser. The embedded browser on .NET Framework
        /// is WebView1 (Internet Explorer), which breaks FIDO keys and Windows Hello and trips
        /// some Conditional Access policies.
        /// </summary>
        public bool UseEmbeddedWebView { get; set; }

        /// <summary>
        /// HWND of the host window (Excel's main window) so the sign-in dialog is parented
        /// correctly and cannot end up behind Excel.
        /// </summary>
        public Func<IntPtr> ParentWindowHandleProvider { get; set; }

        /// <summary>
        /// Where to persist the encrypted MSAL token cache. Leave null to get a path derived
        /// from the full credential identity. Do not point two authenticators that differ in any
        /// way at the same file: each MSAL instance serializes its whole cache, so they would
        /// overwrite one another.
        /// </summary>
        public string TokenCacheFilePath { get; set; }

        /// <summary>The file this credential's tokens are cached in. Public so hosts can report it.</summary>
        public string ResolveTokenCacheFilePath()
        {
            if (!string.IsNullOrWhiteSpace(TokenCacheFilePath))
                return TokenCacheFilePath;

            var authorityHost = new Uri(Cloud.GetAuthorityHost()).Host;

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DataverseDiscovery",
                $"{authorityHost}.{AuthKind}.{Fingerprint()}.msalcache");
        }

        /// <summary>
        /// Everything that changes whose token this is, hashed so the file name stays short and
        /// contains no user name.
        /// </summary>
        private string Fingerprint()
        {
            var discriminator = string.Join("|",
                ClientId?.ToUpperInvariant(),
                TenantId?.ToUpperInvariant(),
                Principal?.ToUpperInvariant());

            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(discriminator));

                var text = new StringBuilder(32);
                for (var i = 0; i < 16; i++)
                    text.Append(hash[i].ToString("x2"));

                return text.ToString();
            }
        }

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(ClientId))
                throw new InvalidOperationException("DataverseAuthOptions.ClientId is required.");
            if (string.IsNullOrWhiteSpace(TenantId))
                throw new InvalidOperationException("DataverseAuthOptions.TenantId is required.");
        }
    }
}

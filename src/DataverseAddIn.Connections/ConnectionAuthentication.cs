using System;
using DataverseAddIn.Discovery;

namespace DataverseAddIn.Connections
{
    /// <summary>
    /// The authentication details a user supplies for one connection. Carries the secret in
    /// memory only — <see cref="DataverseConnectionManager"/> hands it to
    /// <see cref="ISecretStore"/> and stores only the reference on the profile.
    /// </summary>
    public sealed class ConnectionAuthentication
    {
        public DataverseAuthKind Kind { get; set; } = DataverseAuthKind.Interactive;

        public string ClientId { get; set; }

        public string TenantId { get; set; }

        public string UserName { get; set; }

        public string CertificateThumbprint { get; set; }

        public string CertificateStoreLocation { get; set; }

        public string CertificateStoreName { get; set; }

        /// <summary>Null leaves an already-saved secret untouched; use it to keep a secret on edit.</summary>
        public string ClientSecret { get; set; }

        /// <summary>Reads back everything except the secret, which cannot be recovered.</summary>
        public static ConnectionAuthentication FromProfile(ConnectionProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            return new ConnectionAuthentication
            {
                Kind = profile.AuthKind,
                ClientId = profile.ClientId,
                TenantId = profile.TenantId,
                UserName = profile.UserName,
                CertificateThumbprint = profile.CertificateThumbprint,
                CertificateStoreLocation = profile.CertificateStoreLocation,
                CertificateStoreName = profile.CertificateStoreName
            };
        }
    }
}

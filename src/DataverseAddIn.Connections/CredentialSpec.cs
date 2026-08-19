using System;
using DataverseAddIn.Discovery;

namespace DataverseAddIn.Connections
{
    /// <summary>
    /// Identifies one credential. Two connections that resolve to the same spec share a
    /// credential instance, a sign-in and a token cache; two that differ must not.
    /// </summary>
    /// <remarks>
    /// Cloud alone is not enough. A service principal and a signed-in user can target the same
    /// cloud, and so can two different app registrations or two different users, so every field
    /// that changes who the token represents belongs in the key.
    /// </remarks>
    public sealed class CredentialSpec : IEquatable<CredentialSpec>
    {
        public CredentialSpec(
            DataverseCloud cloud,
            DataverseAuthKind kind = DataverseAuthKind.Interactive,
            string clientId = null,
            string tenantId = null,
            string principal = null)
        {
            Cloud = cloud;
            Kind = kind;
            ClientId = Normalize(clientId);
            TenantId = Normalize(tenantId);
            Principal = Normalize(principal);
        }

        public DataverseCloud Cloud { get; }

        public DataverseAuthKind Kind { get; }

        /// <summary>Null to use the host's default app registration for the cloud.</summary>
        public string ClientId { get; }

        /// <summary>Null to use the host's default tenant.</summary>
        public string TenantId { get; }

        /// <summary>
        /// Whatever distinguishes one identity from another within the same app registration:
        /// a user name, a certificate thumbprint, or a secret reference.
        /// </summary>
        public string Principal { get; }

        /// <summary>An interactive sign-in against a cloud, which is what Global Discovery needs.</summary>
        public static CredentialSpec ForDiscovery(DataverseCloud cloud) => new CredentialSpec(cloud);

        private static string Normalize(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        public bool Equals(CredentialSpec other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;

            return Cloud == other.Cloud
                   && Kind == other.Kind
                   && string.Equals(ClientId, other.ClientId, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(TenantId, other.TenantId, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(Principal, other.Principal, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj) => Equals(obj as CredentialSpec);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Cloud * 397;
                hash = (hash ^ (int)Kind) * 397;
                hash = (hash ^ (ClientId?.ToUpperInvariant().GetHashCode() ?? 0)) * 397;
                hash = (hash ^ (TenantId?.ToUpperInvariant().GetHashCode() ?? 0)) * 397;
                return hash ^ (Principal?.ToUpperInvariant().GetHashCode() ?? 0);
            }
        }

        public override string ToString() =>
            $"{Kind} on {Cloud}" +
            (ClientId == null ? null : $", app {ClientId}") +
            (Principal == null ? null : $", as {Principal}");
    }
}

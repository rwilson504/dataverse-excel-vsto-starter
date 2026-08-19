using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using DataverseAddIn.Discovery;

namespace DataverseAddIn.Connections
{
    /// <summary>
    /// Turns a <see cref="CredentialSpec"/> into the credential that implements its kind.
    /// This is the one place that knows the full set, so adding a kind means adding a case here
    /// and a descriptor in <see cref="AuthKindDescriptor"/>.
    /// </summary>
    public sealed class CredentialFactory
    {
        private readonly Func<DataverseCloud, DataverseAuthOptions> _authOptionsFactory;
        private readonly ISecretStore _secrets;

        public CredentialFactory(
            Func<DataverseCloud, DataverseAuthOptions> authOptionsFactory,
            ISecretStore secrets = null)
        {
            _authOptionsFactory = authOptionsFactory ?? throw new ArgumentNullException(nameof(authOptionsFactory));
            _secrets = secrets ?? new DpapiSecretStore();
        }

        public IDataverseCredential Create(CredentialSpec spec) => Create(spec, null);

        /// <summary>
        /// <paramref name="secretOverride"/> bypasses <see cref="ISecretStore"/>, so a secret the
        /// user has typed but not yet saved can be validated before it is written anywhere.
        /// </summary>
        public IDataverseCredential Create(CredentialSpec spec, string secretOverride)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));

            var options = BuildOptions(spec);

            switch (spec.Kind)
            {
                case DataverseAuthKind.Interactive:
                    return new InteractiveCredential(options);

                case DataverseAuthKind.ClientSecret:
                    return new ClientSecretCredential(
                        options,
                        string.IsNullOrEmpty(secretOverride) ? ResolveSecret(spec) : secretOverride);

                case DataverseAuthKind.Certificate:
                    return new CertificateCredential(options, ResolveCertificate(spec));

                default:
                    throw new NotSupportedException(
                        $"Authentication kind '{spec.Kind}' has no implementation. Supported kinds: " +
                        string.Join(", ", AuthKindDescriptor.Supported.Select(d => d.Kind.ToString())) +
                        $". Supply your own factory through {nameof(DataverseConnectionManager)}." +
                        $"{nameof(DataverseConnectionManager.WithCredentials)} to add one.");
            }
        }

        private DataverseAuthOptions BuildOptions(CredentialSpec spec)
        {
            var options = _authOptionsFactory(spec.Cloud)
                          ?? throw new InvalidOperationException($"No authentication options supplied for {spec.Cloud}.");

            options.Cloud = spec.Cloud;
            options.AuthKind = spec.Kind;
            options.Principal = spec.Principal;

            // A per-connection app registration or tenant overrides the host default.
            if (spec.ClientId != null) options.ClientId = spec.ClientId;
            if (spec.TenantId != null) options.TenantId = spec.TenantId;

            return options;
        }

        /// <summary>The spec carries the reference, never the secret; the store holds the value.</summary>
        private string ResolveSecret(CredentialSpec spec)
        {
            if (spec.Principal == null)
                throw new InvalidOperationException("This connection has no client secret saved. Re-enter it.");

            return _secrets.Read(spec.Principal)
                   ?? throw new InvalidOperationException(
                       "The saved client secret could not be read. Secrets are encrypted for the current " +
                       "Windows user on this machine, so a connection copied from elsewhere needs its " +
                       "secret entered again.");
        }

        /// <summary>For a certificate the spec's principal is the thumbprint; the key stays in the store.</summary>
        private static X509Certificate2 ResolveCertificate(CredentialSpec spec)
        {
            if (spec.Principal == null)
                throw new InvalidOperationException("This connection has no certificate thumbprint.");

            return CertificateTokenSource.Find(spec.Principal);
        }
    }
}

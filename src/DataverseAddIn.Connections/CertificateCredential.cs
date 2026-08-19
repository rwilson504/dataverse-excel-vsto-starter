using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using DataverseAddIn.Discovery;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace DataverseAddIn.Connections
{
    /// <summary>
    /// Connects as a registered application using an X.509 certificate rather than a secret.
    /// Preferred over <see cref="ClientSecretCredential"/> where the environment allows it: the
    /// private key stays in the Windows certificate store and is never stored by this add-in.
    /// </summary>
    public sealed class CertificateCredential : IDataverseCredential
    {
        private readonly CertificateTokenSource _tokenSource;

        public CertificateCredential(DataverseAuthOptions options, X509Certificate2 certificate)
            : this(new CertificateTokenSource(options, certificate))
        {
        }

        public CertificateCredential(CertificateTokenSource tokenSource)
        {
            _tokenSource = tokenSource ?? throw new ArgumentNullException(nameof(tokenSource));
        }

        public DataverseAuthKind Kind => DataverseAuthKind.Certificate;

        public DataverseCloud Cloud => _tokenSource.Cloud;

        public IDataverseTokenSource TokenSource => _tokenSource;

        public Task<ServiceClient> CreateClientAsync(
            DataverseEnvironmentReference environment,
            ILogger logger = null,
            CancellationToken cancellationToken = default) =>
            DataverseServiceClientFactory.CreateAsync(_tokenSource, environment, logger, cancellationToken);
    }
}

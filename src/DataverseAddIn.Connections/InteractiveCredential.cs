using System;
using System.Threading;
using System.Threading.Tasks;
using DataverseAddIn.Discovery;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace DataverseAddIn.Connections
{
    /// <summary>
    /// Signs a user in through the system browser and reuses that sign-in for Global Discovery,
    /// the Web API, and <see cref="ServiceClient"/>.
    /// </summary>
    public sealed class InteractiveCredential : IDataverseCredential
    {
        private readonly DataverseAuthenticator _authenticator;

        public InteractiveCredential(DataverseAuthOptions options)
            : this(new DataverseAuthenticator(options ?? throw new ArgumentNullException(nameof(options))))
        {
        }

        /// <summary>Wraps an authenticator the host already owns, so its MSAL cache is shared.</summary>
        public InteractiveCredential(DataverseAuthenticator authenticator)
        {
            _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        }

        public DataverseAuthKind Kind => DataverseAuthKind.Interactive;

        public DataverseCloud Cloud => _authenticator.Cloud;

        public IDataverseTokenSource TokenSource => _authenticator;

        public Task<ServiceClient> CreateClientAsync(
            DataverseEnvironmentReference environment,
            ILogger logger = null,
            CancellationToken cancellationToken = default) =>
            DataverseServiceClientFactory.CreateAsync(_authenticator, environment, logger, cancellationToken);

        public Task SignOutAsync() => _authenticator.SignOutAsync();
    }
}

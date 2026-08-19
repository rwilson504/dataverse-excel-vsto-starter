using System;
using System.Threading;
using System.Threading.Tasks;
using DataverseAddIn.Discovery;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace DataverseAddIn.Connections
{
    /// <summary>
    /// Connects as a registered application rather than a person, using a client secret.
    /// </summary>
    /// <remarks>
    /// Goes through the same token-provider path as an interactive connection instead of the
    /// SDK's <c>AuthType=ClientSecret</c> connection string, so one token serves
    /// <see cref="ServiceClient"/> and the direct Web API clients alike, and the cache is shared.
    /// </remarks>
    public sealed class ClientSecretCredential : IDataverseCredential
    {
        private readonly ClientSecretTokenSource _tokenSource;

        public ClientSecretCredential(DataverseAuthOptions options, string clientSecret)
            : this(new ClientSecretTokenSource(options, clientSecret))
        {
        }

        public ClientSecretCredential(ClientSecretTokenSource tokenSource)
        {
            _tokenSource = tokenSource ?? throw new ArgumentNullException(nameof(tokenSource));
        }

        public DataverseAuthKind Kind => DataverseAuthKind.ClientSecret;

        public DataverseCloud Cloud => _tokenSource.Cloud;

        public IDataverseTokenSource TokenSource => _tokenSource;

        public Task<ServiceClient> CreateClientAsync(
            DataverseEnvironmentReference environment,
            ILogger logger = null,
            CancellationToken cancellationToken = default) =>
            DataverseServiceClientFactory.CreateAsync(_tokenSource, environment, logger, cancellationToken);
    }
}

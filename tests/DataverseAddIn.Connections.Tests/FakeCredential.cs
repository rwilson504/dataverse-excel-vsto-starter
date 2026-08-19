using System;
using System.Threading;
using System.Threading.Tasks;
using DataverseAddIn.Discovery;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace DataverseAddIn.Connections.Tests
{
    /// <summary>
    /// Stands in for a real credential everywhere except <see cref="CreateClientAsync"/>, which
    /// cannot be faked: <see cref="IDataverseCredential"/> returns a concrete
    /// <see cref="ServiceClient"/>, and one of those needs a live environment.
    /// </summary>
    internal sealed class FakeCredential : IDataverseCredential
    {
        public FakeCredential(
            DataverseCloud cloud = DataverseCloud.Commercial,
            DataverseAuthKind kind = DataverseAuthKind.Interactive,
            IDataverseTokenSource tokenSource = null)
        {
            Cloud = cloud;
            Kind = kind;
            TokenSource = tokenSource;
        }

        /// <summary>A credential with no bearer token, as on-premises AD and IFD will be.</summary>
        public static FakeCredential WithoutToken(
            DataverseCloud cloud = DataverseCloud.Commercial,
            DataverseAuthKind kind = DataverseAuthKind.WindowsIntegrated) =>
            new FakeCredential(cloud, kind);

        public DataverseAuthKind Kind { get; }

        public DataverseCloud Cloud { get; }

        public IDataverseTokenSource TokenSource { get; }

        public Task<ServiceClient> CreateClientAsync(
            DataverseEnvironmentReference environment,
            ILogger logger = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Connecting needs a live environment and is out of scope for unit tests.");
    }
}

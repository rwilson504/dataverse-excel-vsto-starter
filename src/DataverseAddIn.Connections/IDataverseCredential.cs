using System.Threading;
using System.Threading.Tasks;
using DataverseAddIn.Discovery;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace DataverseAddIn.Connections
{
    /// <summary>
    /// One authentication kind, bound to one cloud, able to produce a connected
    /// <see cref="ServiceClient"/>. Adding an authentication kind means adding an
    /// implementation of this and nothing else.
    /// </summary>
    public interface IDataverseCredential
    {
        DataverseAuthKind Kind { get; }

        DataverseCloud Cloud { get; }

        /// <summary>
        /// Null when the kind has no bearer token, which is the case for on-premises Active
        /// Directory and IFD. Those reach Dataverse only through <see cref="CreateClientAsync"/>,
        /// so Global Discovery and the direct Web API clients are unavailable to them.
        /// </summary>
        IDataverseTokenSource TokenSource { get; }

        Task<ServiceClient> CreateClientAsync(
            DataverseEnvironmentReference environment,
            ILogger logger = null,
            CancellationToken cancellationToken = default);
    }
}

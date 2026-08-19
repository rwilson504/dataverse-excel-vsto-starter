using System;
using System.Threading;
using System.Threading.Tasks;

namespace DataverseAddIn.Discovery
{
    /// <summary>
    /// Supplies bearer tokens for Dataverse resources. Everything that talks to Dataverse over
    /// HTTP depends on this rather than on a specific authentication mechanism, so a new
    /// authentication type is a new implementation and nothing else changes.
    /// </summary>
    /// <remarks>
    /// Not every authentication type can implement this. On-premises Active Directory and IFD
    /// connections have no bearer token at all and reach Dataverse only through
    /// <c>ServiceClient</c>; those are modelled elsewhere and simply have no token source.
    /// </remarks>
    public interface IDataverseTokenSource
    {
        /// <summary>The cloud this source issues tokens for. Tokens are not portable across clouds.</summary>
        DataverseCloud Cloud { get; }

        /// <summary>
        /// True when acquiring a token can put UI on screen. Callers on an STA UI thread must
        /// force acquisition onto an async path before handing the source to a synchronous
        /// consumer, or the prompt deadlocks the host.
        /// </summary>
        bool IsInteractive { get; }

        /// <summary>
        /// False when the credential cannot call the Global Discovery Service, which is the
        /// normal case for service principals and for on-premises deployments. Callers should
        /// fall back to asking the user for an environment URL.
        /// </summary>
        bool SupportsGlobalDiscovery { get; }

        /// <summary>
        /// A bearer token for <paramref name="resourceUrl"/>. Dataverse tokens are per-resource:
        /// Global Discovery and each environment are separate resources.
        /// </summary>
        Task<string> GetTokenAsync(string resourceUrl, CancellationToken cancellationToken = default);

        /// <summary>Discards any cached credentials. A no-op for sources that hold none.</summary>
        Task SignOutAsync();
    }

    public static class DataverseTokenSourceExtensions
    {
        /// <summary>Token for the Global Discovery Service of the source's own cloud.</summary>
        public static Task<string> GetDiscoveryTokenAsync(
            this IDataverseTokenSource tokenSource, CancellationToken cancellationToken = default)
        {
            if (tokenSource == null) throw new ArgumentNullException(nameof(tokenSource));

            if (!tokenSource.SupportsGlobalDiscovery)
            {
                throw new NotSupportedException(
                    $"This credential cannot call the Global Discovery Service for {tokenSource.Cloud}. " +
                    "Supply an environment URL instead.");
            }

            return tokenSource.GetTokenAsync(tokenSource.Cloud.GetGlobalDiscoveryUrl(), cancellationToken);
        }
    }
}

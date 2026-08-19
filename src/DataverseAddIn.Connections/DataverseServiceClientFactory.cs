using System;
using System.Threading;
using System.Threading.Tasks;
using DataverseAddIn.Discovery;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace DataverseAddIn.Connections
{
    /// <summary>
    /// Builds a <see cref="ServiceClient"/> (an <c>IOrganizationService</c>) for an
    /// environment, reusing the MSAL sign-in already performed for discovery.
    /// </summary>
    public static class DataverseServiceClientFactory
    {
        /// <summary>Connect to an environment chosen from Global Discovery.</summary>
        public static Task<ServiceClient> CreateAsync(
            IDataverseTokenSource tokenSource,
            DataverseInstance instance,
            ILogger logger = null,
            CancellationToken cancellationToken = default) =>
            CreateAsync(tokenSource, DataverseEnvironmentReference.FromInstance(instance), logger, cancellationToken);

        /// <summary>Connect to an environment URL supplied by the user, skipping discovery.</summary>
        public static Task<ServiceClient> CreateAsync(
            IDataverseTokenSource tokenSource,
            string environmentUrl,
            ILogger logger = null,
            CancellationToken cancellationToken = default) =>
            CreateAsync(tokenSource, DataverseEnvironmentReference.Parse(environmentUrl), logger, cancellationToken);

        public static async Task<ServiceClient> CreateAsync(
            IDataverseTokenSource tokenSource,
            DataverseEnvironmentReference environment,
            ILogger logger = null,
            CancellationToken cancellationToken = default)
        {
            if (tokenSource == null) throw new ArgumentNullException(nameof(tokenSource));
            if (environment == null) throw new ArgumentNullException(nameof(environment));

            NetworkDefaults.Ensure();

            if (environment.Cloud != tokenSource.Cloud)
            {
                throw new InvalidOperationException(
                    $"{environment.Url} is in {environment.Cloud}, but this credential is configured for " +
                    $"{tokenSource.Cloud}. Use the credential built for {environment.Cloud} — the clouds " +
                    "may not even share an identity authority.");
            }

            // Force any interactive prompt to happen here, on the async path. ServiceClient
            // calls the token provider synchronously, which would deadlock Excel's STA UI thread.
            if (tokenSource.IsInteractive)
                await tokenSource.GetTokenAsync(environment.Url, cancellationToken).ConfigureAwait(false);

            var client = await Task.Run(() => new ServiceClient(
                    instanceUrl: new Uri(environment.Url),
                    tokenProviderFunction: TokenProvider(tokenSource),
                    useUniqueInstance: true,
                    logger: logger),
                cancellationToken).ConfigureAwait(false);

            if (!client.IsReady)
            {
                var message = client.LastError;
                var inner = client.LastException;
                client.Dispose();

                throw new InvalidOperationException(
                    $"Could not connect to {environment.Url}. {message}".TrimEnd(), inner);
            }

            return client;
        }

        /// <summary>
        /// ServiceClient passes the instance URI on every call and expects a current token, so
        /// this must re-ask the token source rather than capture one. MSAL serves it from cache.
        /// </summary>
        private static Func<string, Task<string>> TokenProvider(IDataverseTokenSource tokenSource) =>
            instanceUri => tokenSource.GetTokenAsync(instanceUri);
    }
}

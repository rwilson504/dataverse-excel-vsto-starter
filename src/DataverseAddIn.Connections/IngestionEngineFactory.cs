using System;
using DataverseAddIn.Ingestion;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace DataverseAddIn.Connections
{
    /// <summary>
    /// Bridges a connected <see cref="ServiceClient"/> to the host-agnostic ingestion engine.
    /// This is the only place the two assemblies meet.
    /// </summary>
    public static class IngestionEngineFactory
    {
        /// <summary>
        /// Builds an engine that clones the client per worker thread and honours the
        /// environment's recommended degree of parallelism.
        /// </summary>
        /// <param name="client">A connected client. Its affinity cookie is disabled.</param>
        public static DataverseIngestionEngine CreateEngine(this ServiceClient client, IngestionOptions options = null)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));

            if (!client.IsReady)
                throw new InvalidOperationException("The ServiceClient is not connected.");

            // Service protection limits are applied per web server, so spreading requests
            // across all of them raises the effective ceiling. Only safe because this client
            // is used for bulk work, not for cached interactive reads.
            client.EnableAffinityCookie = false;

            var dop = client.RecommendedDegreesOfParallelism > 0
                ? client.RecommendedDegreesOfParallelism
                : 1;

            return new DataverseIngestionEngine(() => client.Clone(), dop, options);
        }
    }
}

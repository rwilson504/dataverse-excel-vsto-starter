using System.Net;

namespace DataverseAddIn.Discovery
{
    /// <summary>
    /// Process-wide networking defaults, applied wherever a connection is made.
    /// </summary>
    /// <remarks>
    /// These settings are per-process, not per-client, so putting them in one class and relying
    /// on that class being constructed makes success depend on which feature the user happened
    /// to use first — discovery enabled TLS 1.2 for everything that followed, and connecting
    /// straight to a saved environment did not.
    /// </remarks>
    public static class NetworkDefaults
    {
        public static void Ensure()
        {
            // |= rather than =, so nothing a host already enabled is removed.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }
    }
}

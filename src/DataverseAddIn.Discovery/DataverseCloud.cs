using System;

namespace DataverseAddIn.Discovery
{
    /// <summary>
    /// The Dataverse clouds that expose a Global Discovery Service endpoint.
    /// </summary>
    public enum DataverseCloud
    {
        /// <summary>Public/commercial cloud. Used by most private-sector tenants.</summary>
        Commercial = 0,

        /// <summary>US Government Community Cloud (GCC).</summary>
        UsGovernmentCommunity = 1,

        /// <summary>US Government (GCC High).</summary>
        UsGovernmentHigh = 2,

        /// <summary>US Department of Defense.</summary>
        UsDepartmentOfDefense = 3,

        /// <summary>China cloud operated by 21Vianet.</summary>
        China = 4
    }

    public static class DataverseCloudExtensions
    {
        /// <summary>
        /// Root of the Global Discovery Service for the cloud. This doubles as the
        /// OAuth resource, so the scope is this value + "/user_impersonation".
        /// </summary>
        public static string GetGlobalDiscoveryUrl(this DataverseCloud cloud)
        {
            switch (cloud)
            {
                case DataverseCloud.Commercial: return "https://globaldisco.crm.dynamics.com";
                case DataverseCloud.UsGovernmentCommunity: return "https://globaldisco.crm9.dynamics.com";
                case DataverseCloud.UsGovernmentHigh: return "https://globaldisco.crm.microsoftdynamics.us";
                case DataverseCloud.UsDepartmentOfDefense: return "https://globaldisco.crm.appsplatform.us";
                case DataverseCloud.China: return "https://globaldisco.crm.dynamics.cn";
                default: throw new ArgumentOutOfRangeException(nameof(cloud));
            }
        }

        /// <summary>
        /// Microsoft Entra ID login host that issues tokens for the cloud.
        /// GCC deliberately shares the public host: Dynamics 365 GCC uses public Microsoft
        /// Entra ID for identity, only its Dataverse endpoints are separate. GCC High and DoD
        /// require Microsoft Entra Government.
        /// </summary>
        public static string GetAuthorityHost(this DataverseCloud cloud)
        {
            switch (cloud)
            {
                case DataverseCloud.Commercial:
                case DataverseCloud.UsGovernmentCommunity:
                    return "https://login.microsoftonline.com";
                case DataverseCloud.UsGovernmentHigh:
                case DataverseCloud.UsDepartmentOfDefense:
                    return "https://login.microsoftonline.us";
                case DataverseCloud.China:
                    return "https://login.chinacloudapi.cn";
                default: throw new ArgumentOutOfRangeException(nameof(cloud));
            }
        }

        /// <summary>Name for UI, using the terms people actually say.</summary>
        public static string GetDisplayName(this DataverseCloud cloud)
        {
            switch (cloud)
            {
                case DataverseCloud.Commercial: return "Commercial (public)";
                case DataverseCloud.UsGovernmentCommunity: return "US Government Community Cloud (GCC)";
                case DataverseCloud.UsGovernmentHigh: return "US Government High (GCC High)";
                case DataverseCloud.UsDepartmentOfDefense: return "US Department of Defense (DoD)";
                case DataverseCloud.China: return "China (operated by 21Vianet)";
                default: throw new ArgumentOutOfRangeException(nameof(cloud));
            }
        }

        /// <summary>
        /// True when the cloud authenticates against Microsoft Entra Government rather than
        /// public Microsoft Entra ID, which means it needs its own app registration and its
        /// own user sign-in. The two identity clouds are separate directories.
        /// </summary>
        public static bool UsesGovernmentIdentity(this DataverseCloud cloud) =>
            cloud == DataverseCloud.UsGovernmentHigh || cloud == DataverseCloud.UsDepartmentOfDefense;

        /// <summary>
        /// Clouds that share an identity authority, and therefore a single sign-in and a single
        /// app registration. Discovery still has to be called once per cloud.
        /// </summary>
        public static bool SharesIdentityWith(this DataverseCloud cloud, DataverseCloud other) =>
            string.Equals(cloud.GetAuthorityHost(), other.GetAuthorityHost(), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Infers the cloud from an environment or discovery URL, so a persisted environment
        /// choice can be resolved back to the right authenticator.
        /// </summary>
        public static bool TryGetCloudFromUrl(string url, out DataverseCloud cloud)
        {
            cloud = DataverseCloud.Commercial;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            var host = uri.Host;

            if (host.EndsWith(".microsoftdynamics.us", StringComparison.OrdinalIgnoreCase))
            {
                cloud = DataverseCloud.UsGovernmentHigh;
                return true;
            }

            if (host.EndsWith(".appsplatform.us", StringComparison.OrdinalIgnoreCase))
            {
                cloud = DataverseCloud.UsDepartmentOfDefense;
                return true;
            }

            if (host.EndsWith(".dynamics.cn", StringComparison.OrdinalIgnoreCase))
            {
                cloud = DataverseCloud.China;
                return true;
            }

            if (host.EndsWith(".dynamics.com", StringComparison.OrdinalIgnoreCase))
            {
                // GCC lives under the crm9 region label but still on the dynamics.com suffix.
                cloud = host.IndexOf(".crm9.", StringComparison.OrdinalIgnoreCase) >= 0
                    ? DataverseCloud.UsGovernmentCommunity
                    : DataverseCloud.Commercial;
                return true;
            }

            return false;
        }
    }
}

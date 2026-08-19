using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;

namespace DataverseAddIn.Discovery
{
    /// <summary>
    /// One environment returned by the Global Discovery Service <c>Instances</c> entity set.
    /// </summary>
    /// <remarks>
    /// Date and Guid values are kept as <see cref="string"/> because
    /// <see cref="System.Runtime.Serialization.Json.DataContractJsonSerializer"/> expects the
    /// legacy <c>\/Date(...)\/</c> form, not the ISO 8601 that Dataverse returns. Typed
    /// convenience properties are provided below.
    /// </remarks>
    [DataContract]
    public sealed class DataverseInstance
    {
        /// <summary>Base URL to use for Web API and SDK calls, e.g. <c>https://contoso.api.crm.dynamics.com</c>.</summary>
        [DataMember(Name = "ApiUrl")]
        public string ApiUrl { get; set; }

        /// <summary>Application URL, e.g. <c>https://contoso.crm.dynamics.com</c>.</summary>
        [DataMember(Name = "Url")]
        public string Url { get; set; }

        /// <summary>Display name shown in maker portals. Use this in your UI.</summary>
        [DataMember(Name = "FriendlyName")]
        public string FriendlyName { get; set; }

        /// <summary>The OrganizationId.</summary>
        [DataMember(Name = "Id")]
        public string Id { get; set; }

        [DataMember(Name = "UniqueName")]
        public string UniqueName { get; set; }

        [DataMember(Name = "UrlName")]
        public string UrlName { get; set; }

        [DataMember(Name = "EnvironmentId")]
        public string EnvironmentId { get; set; }

        [DataMember(Name = "TenantId")]
        public string TenantId { get; set; }

        [DataMember(Name = "Version")]
        public string Version { get; set; }

        /// <summary>Two or three letter region code, e.g. <c>NA</c>, <c>EMEA</c>.</summary>
        [DataMember(Name = "Region")]
        public string Region { get; set; }

        [DataMember(Name = "DatacenterId")]
        public string DatacenterId { get; set; }

        [DataMember(Name = "DatacenterName")]
        public string DatacenterName { get; set; }

        [DataMember(Name = "Purpose")]
        public string Purpose { get; set; }

        /// <summary>Whether the calling user holds the System Administrator role in this environment.</summary>
        [DataMember(Name = "IsUserSysAdmin")]
        public bool IsUserSysAdmin { get; set; }

        /// <summary><c>0</c> = enabled, <c>1</c> = disabled.</summary>
        [DataMember(Name = "State")]
        public int State { get; set; }

        /// <summary>See <c>OrganizationType</c> in the Dataverse Web API reference.</summary>
        [DataMember(Name = "OrganizationType")]
        public int OrganizationType { get; set; }

        [DataMember(Name = "StatusMessage")]
        public int StatusMessage { get; set; }

        [DataMember(Name = "LastUpdated")]
        public string LastUpdatedRaw { get; set; }

        [DataMember(Name = "TrialExpirationDate")]
        public string TrialExpirationDateRaw { get; set; }

        /// <summary>
        /// Cloud this environment was discovered in. Not part of the OData payload; stamped by
        /// <see cref="GlobalDiscoveryClient"/> so callers know which authenticator to use when
        /// requesting a token for <see cref="ApiUrl"/>.
        /// </summary>
        public DataverseCloud Cloud { get; internal set; }

        public bool IsEnabled => State == 0;

        public Guid? OrganizationId => ParseGuid(Id);

        public Guid? Tenant => ParseGuid(TenantId);

        public DateTimeOffset? LastUpdated => ParseDate(LastUpdatedRaw);

        public DateTimeOffset? TrialExpirationDate => ParseDate(TrialExpirationDateRaw);

        public override string ToString() => $"{FriendlyName} ({ApiUrl})";

        private static Guid? ParseGuid(string value) =>
            Guid.TryParse(value, out var g) ? g : (Guid?)null;

        private static DateTimeOffset? ParseDate(string value) =>
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var d) ? d : (DateTimeOffset?)null;
    }

    /// <summary>OData envelope: <c>{ "@odata.context": "...", "value": [ ... ] }</c>.</summary>
    [DataContract]
    internal sealed class DataverseInstanceCollection
    {
        [DataMember(Name = "value")]
        public List<DataverseInstance> Value { get; set; }
    }
}

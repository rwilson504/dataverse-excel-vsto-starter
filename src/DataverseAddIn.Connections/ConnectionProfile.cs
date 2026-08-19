using System;
using System.Runtime.Serialization;
using DataverseAddIn.Discovery;

namespace DataverseAddIn.Connections
{
    /// <summary>A saved connection in the connection manager.</summary>
    [DataContract]
    public sealed class ConnectionProfile
    {
        [DataMember(Name = "Id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [DataMember(Name = "Name")]
        public string Name { get; set; }

        /// <summary>Scheme and host only. Also the OAuth resource.</summary>
        [DataMember(Name = "EnvironmentUrl")]
        public string EnvironmentUrl { get; set; }

        /// <summary>Stored by name so reordering the enum cannot corrupt saved profiles.</summary>
        [DataMember(Name = "Cloud")]
        public string CloudName { get; set; }

        /// <summary>
        /// Hex colour used to tag the environment in the UI, e.g. <c>#C62828</c>. Held as a
        /// string so the model stays free of a System.Drawing reference.
        /// </summary>
        [DataMember(Name = "Color")]
        public string Color { get; set; }

        /// <summary>
        /// True while <see cref="Name"/> is a placeholder derived from the URL. The first
        /// successful connection replaces it with the organization's friendly name.
        /// </summary>
        [DataMember(Name = "NameIsAuto")]
        public bool NameIsAuto { get; set; }

        /// <summary>Stored by name. Absent on profiles saved before multiple kinds existed.</summary>
        [DataMember(Name = "AuthKind")]
        public string AuthKindName { get; set; }

        /// <summary>App registration to authenticate with. Null uses the host's default.</summary>
        [DataMember(Name = "ClientId")]
        public string ClientId { get; set; }

        /// <summary>Tenant to sign in against. Null uses the host's default.</summary>
        [DataMember(Name = "TenantId")]
        public string TenantId { get; set; }

        /// <summary>Login hint for interactive sign-in, or the account for username/password.</summary>
        [DataMember(Name = "UserName")]
        public string UserName { get; set; }

        [DataMember(Name = "CertThumbprint")]
        public string CertificateThumbprint { get; set; }

        /// <summary><c>CurrentUser</c> or <c>LocalMachine</c>. Null means <c>CurrentUser</c>.</summary>
        [DataMember(Name = "CertStoreLocation")]
        public string CertificateStoreLocation { get; set; }

        /// <summary>Certificate store name, e.g. <c>My</c>. Null means <c>My</c>.</summary>
        [DataMember(Name = "CertStoreName")]
        public string CertificateStoreName { get; set; }

        /// <summary>
        /// Key into <see cref="ISecretStore"/>. The secret itself is never written here — this
        /// file is plain JSON in the roaming profile.
        /// </summary>
        [DataMember(Name = "SecretRef")]
        public string SecretRef { get; set; }

        public DataverseCloud Cloud =>
            Enum.TryParse<DataverseCloud>(CloudName, ignoreCase: true, result: out var cloud)
                ? cloud
                : DataverseCloud.Commercial;

        /// <summary>Profiles saved before this field existed are interactive sign-ins.</summary>
        public DataverseAuthKind AuthKind =>
            Enum.TryParse<DataverseAuthKind>(AuthKindName, ignoreCase: true, result: out var kind)
                ? kind
                : DataverseAuthKind.Interactive;

        public CredentialSpec ToCredentialSpec() =>
            new CredentialSpec(Cloud, AuthKind, ClientId, TenantId, ResolvePrincipal());

        /// <summary>What separates this identity from another using the same app registration.</summary>
        private string ResolvePrincipal()
        {
            switch (AuthKind)
            {
                case DataverseAuthKind.Certificate: return CertificateThumbprint;
                case DataverseAuthKind.ClientSecret: return SecretRef;
                default: return UserName;
            }        }

        public static ConnectionProfile Create(
            string name,
            DataverseEnvironmentReference environment,
            string color = null,
            DataverseAuthKind authKind = DataverseAuthKind.Interactive)
        {
            if (environment == null) throw new ArgumentNullException(nameof(environment));

            var auto = string.IsNullOrWhiteSpace(name);

            return new ConnectionProfile
            {
                Name = auto ? SuggestName(environment) : name.Trim(),
                NameIsAuto = auto,
                EnvironmentUrl = environment.Url,
                CloudName = environment.Cloud.ToString(),
                AuthKindName = authKind.ToString(),
                Color = color
            };
        }

        /// <summary>First host label, e.g. <c>contoso</c> from <c>contoso.crm.dynamics.com</c>.</summary>
        public static string SuggestName(DataverseEnvironmentReference environment)
        {
            if (environment == null) throw new ArgumentNullException(nameof(environment));

            var host = new Uri(environment.Url).Host;
            var separator = host.IndexOf('.');

            return separator > 0 ? host.Substring(0, separator) : host;
        }

        public DataverseEnvironmentReference ToEnvironmentReference() =>
            DataverseEnvironmentReference.Parse(EnvironmentUrl);
        /// <summary>
        /// Replaces a placeholder name with the organization's own, once it has identified
        /// itself. Returns whether anything changed, so the caller knows to persist.
        /// A name the user chose is never overwritten.
        /// </summary>
        public bool AdoptOrganizationName(string friendlyName)
        {
            if (!NameIsAuto || string.IsNullOrWhiteSpace(friendlyName))
                return false;

            Name = friendlyName.Trim();
            NameIsAuto = false;

            return true;
        }
        public override string ToString() => $"{Name}  —  {EnvironmentUrl}  ({Cloud})";    }
}

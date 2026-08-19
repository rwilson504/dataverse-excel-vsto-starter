using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataverseAddIn.Discovery;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace DataverseAddIn.Connections
{
    /// <summary>
    /// Owns saved connections, the per-cloud credentials, and the single active
    /// <see cref="ServiceClient"/>. Create one per host (add-in) and keep it alive.
    /// </summary>
    public sealed class DataverseConnectionManager : IDisposable
    {
        private readonly Func<CredentialSpec, IDataverseCredential> _credentialFactory;
        private readonly CredentialFactory _ownedFactory;
        private readonly Dictionary<CredentialSpec, IDataverseCredential> _credentials =
            new Dictionary<CredentialSpec, IDataverseCredential>();
        private readonly ConnectionStore _store;
        private readonly ISecretStore _secrets;

        /// <summary>Every kind <see cref="CredentialFactory"/> implements, which is the usual case.</summary>
        public DataverseConnectionManager(
            Func<DataverseCloud, DataverseAuthOptions> authOptionsFactory,
            ConnectionStore store = null,
            ISecretStore secrets = null)
        {
            if (authOptionsFactory == null) throw new ArgumentNullException(nameof(authOptionsFactory));

            _store = store ?? new ConnectionStore();
            _secrets = secrets ?? new DpapiSecretStore();
            _ownedFactory = new CredentialFactory(authOptionsFactory, _secrets);
            _credentialFactory = _ownedFactory.Create;
        }

        /// <summary>
        /// Lets the host supply kinds <see cref="CredentialFactory"/> does not implement. A
        /// separate factory method rather than a constructor overload, because a lambda would be
        /// ambiguous between the two.
        /// </summary>
        public static DataverseConnectionManager WithCredentials(
            Func<CredentialSpec, IDataverseCredential> credentialFactory,
            ConnectionStore store = null,
            ISecretStore secrets = null) =>
            new DataverseConnectionManager(credentialFactory, store, secrets);

        private DataverseConnectionManager(
            Func<CredentialSpec, IDataverseCredential> credentialFactory,
            ConnectionStore store,
            ISecretStore secrets)
        {
            _credentialFactory = credentialFactory ?? throw new ArgumentNullException(nameof(credentialFactory));
            _store = store ?? new ConnectionStore();
            _secrets = secrets ?? new DpapiSecretStore();
        }

        /// <summary>Raised whenever the active connection changes, so hosts can refresh UI state.</summary>
        public event EventHandler ConnectionChanged;

        public IReadOnlyList<ConnectionProfile> Profiles => _store.Profiles;

        /// <summary>Where connection secrets live. Profiles hold only a reference to them.</summary>
        public ISecretStore Secrets => _secrets;

        public ConnectionProfile CurrentProfile { get; private set; }

        public ServiceClient Current { get; private set; }

        public bool IsConnected => Current != null && Current.IsReady;

        public ConnectionProfile Add(
            string name, string environmentUrl, string color = null, ConnectionAuthentication authentication = null)
        {
            if (!DataverseEnvironmentReference.TryParse(environmentUrl, out var environment, out var error))
                throw new FormatException(error);

            return Add(ConnectionProfile.Create(name, environment, color), authentication);
        }

        /// <summary>Named apart from <see cref="Add(string,string,string,ConnectionAuthentication)"/> because a null name made the pair ambiguous.</summary>
        public ConnectionProfile AddDiscovered(
            DataverseInstance instance, string name = null, string color = null, ConnectionAuthentication authentication = null)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));

            return Add(
                ConnectionProfile.Create(
                    string.IsNullOrWhiteSpace(name) ? instance.FriendlyName : name,
                    DataverseEnvironmentReference.FromInstance(instance),
                    color),
                authentication);
        }

        /// <summary>Persists in-place edits, such as a rename or a colour change.</summary>
        public void Update(ConnectionProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            _store.Save();
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Applies a new authentication choice, moving any secret into <see cref="Secrets"/>
        /// and discarding one the connection no longer needs.
        /// </summary>
        public void UpdateAuthentication(ConnectionProfile profile, ConnectionAuthentication authentication)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            ApplyAuthentication(profile, authentication);
            Update(profile);
        }

        private ConnectionProfile Add(ConnectionProfile profile, ConnectionAuthentication authentication = null)
        {
            ApplyAuthentication(profile, authentication);
            _store.Add(profile);

            return profile;
        }

        private void ApplyAuthentication(ConnectionProfile profile, ConnectionAuthentication authentication)
        {
            if (authentication == null) return;

            profile.AuthKindName = authentication.Kind.ToString();
            profile.ClientId = authentication.ClientId;
            profile.TenantId = authentication.TenantId;
            profile.UserName = authentication.UserName;
            profile.CertificateThumbprint = authentication.CertificateThumbprint;
            profile.CertificateStoreLocation = authentication.CertificateStoreLocation;
            profile.CertificateStoreName = authentication.CertificateStoreName;

            if (authentication.Kind != DataverseAuthKind.ClientSecret)
            {
                // Nothing else reads it, and leaving it behind is an avoidable secret at rest.
                _secrets.Delete(profile.SecretRef);
                profile.SecretRef = null;
                return;
            }

            // A blank secret on edit means "keep the one already saved".
            if (!string.IsNullOrEmpty(authentication.ClientSecret))
                profile.SecretRef = _secrets.Write(profile.SecretRef, authentication.ClientSecret);
        }

        public void Delete(ConnectionProfile profile)
        {
            if (profile == null) return;

            if (CurrentProfile != null && string.Equals(CurrentProfile.Id, profile.Id, StringComparison.Ordinal))
                Disconnect();

            _store.Remove(profile);
            _secrets.Delete(profile.SecretRef);
        }

        public bool AlreadySaved(string environmentUrl) =>
            DataverseEnvironmentReference.TryParse(environmentUrl, out var environment, out _) &&
            _store.ContainsUrl(environment.Url);

        /// <summary>Lists environments from Global Discovery for one cloud.</summary>
        public async Task<IReadOnlyList<DataverseInstance>> DiscoverAsync(
            DataverseCloud cloud, CancellationToken cancellationToken = default)
        {
            using (var discovery = new GlobalDiscoveryClient(GetTokenSource(CredentialSpec.ForDiscovery(cloud))))
                return await discovery.GetInstancesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task ConnectAsync(ConnectionProfile profile, CancellationToken cancellationToken = default)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            var environment = profile.ToEnvironmentReference();
            var credential = GetCredential(profile.ToCredentialSpec());

            if (environment.Cloud != credential.Cloud)
            {
                throw new InvalidOperationException(
                    $"The credential supplied for {environment.Cloud} is configured for {credential.Cloud}.");
            }

            var client = await credential
                .CreateClientAsync(environment, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            Disconnect();

            Current = client;
            CurrentProfile = profile;

            if (profile.AdoptOrganizationName(client.ConnectedOrgFriendlyName))
                _store.Save();

            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Connects with the supplied details and disconnects again, so a connection can be
        /// validated before it is saved. Nothing is cached or persisted: the credential is built
        /// fresh, and a secret typed but not yet stored is used directly.
        /// </summary>
        /// <param name="existingSecretRef">
        /// Used when the caller left the secret blank to keep an already-saved one.
        /// </param>
        /// <returns>A description of what was reached, for display.</returns>
        public async Task<string> TestAsync(
            DataverseEnvironmentReference environment,
            ConnectionAuthentication authentication,
            string existingSecretRef = null,
            CancellationToken cancellationToken = default)
        {
            if (environment == null) throw new ArgumentNullException(nameof(environment));
            if (authentication == null) throw new ArgumentNullException(nameof(authentication));

            var credential = BuildTestCredential(environment, authentication, existingSecretRef);

            if (environment.Cloud != credential.Cloud)
            {
                throw new InvalidOperationException(
                    $"{environment.Url} is in {environment.Cloud}, but this credential is for {credential.Cloud}.");
            }

            using (var client = await credential
                       .CreateClientAsync(environment, cancellationToken: cancellationToken)
                       .ConfigureAwait(false))
            {
                // Connecting proves the identity resolves. WhoAmI proves the account exists in
                // *this* environment and holds a security role — the part that fails for a
                // service principal with no application user, and the reason to test at all.
                var who = (WhoAmIResponse)client.Execute(new WhoAmIRequest());

                var name = string.IsNullOrWhiteSpace(client.ConnectedOrgFriendlyName)
                    ? environment.Url
                    : client.ConnectedOrgFriendlyName;

                return $"Connected to {name} as user {who.UserId:D}.";
            }
        }

        private IDataverseCredential BuildTestCredential(
            DataverseEnvironmentReference environment,
            ConnectionAuthentication authentication,
            string existingSecretRef)
        {
            var principal = authentication.Kind == DataverseAuthKind.ClientSecret
                ? existingSecretRef
                : authentication.UserName;

            var spec = new CredentialSpec(
                environment.Cloud, authentication.Kind, authentication.ClientId, authentication.TenantId, principal);

            if (_ownedFactory != null)
                return _ownedFactory.Create(spec, authentication.ClientSecret);

            if (!string.IsNullOrEmpty(authentication.ClientSecret))
            {
                throw new NotSupportedException(
                    "This connection manager was built with a custom credential factory, which cannot be " +
                    "given an unsaved secret. Save the connection first, then connect.");
            }

            return _credentialFactory(spec)
                   ?? throw new InvalidOperationException($"No credential supplied for {spec}.");
        }

        public void Disconnect()
        {
            var hadConnection = Current != null;
            Current?.Dispose();
            Current = null;
            CurrentProfile = null;

            if (hadConnection)
                ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// One credential per distinct identity, cached. Keyed on the whole
        /// <see cref="CredentialSpec"/>: two credentials on the same cloud that differ in kind,
        /// app registration, tenant or principal are different sign-ins and must not share.
        /// </summary>
        public IDataverseCredential GetCredential(CredentialSpec spec)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));

            if (_credentials.TryGetValue(spec, out var existing))
                return existing;

            var credential = _credentialFactory(spec)
                             ?? throw new InvalidOperationException($"No credential supplied for {spec}.");

            _credentials[spec] = credential;

            return credential;
        }

        /// <summary>
        /// The token source behind a credential, for Global Discovery and direct Web API calls.
        /// Throws for kinds that have no bearer token, such as on-premises AD and IFD.
        /// </summary>
        public IDataverseTokenSource GetTokenSource(CredentialSpec spec)
        {
            var credential = GetCredential(spec);

            return credential.TokenSource
                   ?? throw new NotSupportedException(
                       $"{credential.Kind} connections have no bearer token, so they cannot be used for " +
                       "Global Discovery or direct Web API calls.");
        }

        public void Dispose() => Disconnect();
    }
}

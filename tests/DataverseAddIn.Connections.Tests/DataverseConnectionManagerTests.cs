using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataverseAddIn.Discovery;
using Xunit;

namespace DataverseAddIn.Connections.Tests
{
    public class DataverseConnectionManagerTests
    {
        private static ConnectionStore StoreIn(TempDirectory dir) =>
            new ConnectionStore(dir.File("connections.json"));

        private static CredentialSpec Spec(
            DataverseCloud cloud = DataverseCloud.Commercial,
            DataverseAuthKind kind = DataverseAuthKind.Interactive,
            string clientId = "app-1",
            string principal = "user@contoso.com") =>
            new CredentialSpec(cloud, kind, clientId, "tenant-1", principal);

        [Fact]
        public void Builds_a_credential_once_per_identity()
        {
            using (var dir = new TempDirectory())
            {
                var calls = new List<CredentialSpec>();
                var manager = DataverseConnectionManager.WithCredentials(
                    spec => { calls.Add(spec); return new FakeCredential(spec.Cloud, spec.Kind); },
                    StoreIn(dir));

                var first = manager.GetCredential(Spec());
                var again = manager.GetCredential(Spec());

                Assert.Same(first, again);
                Assert.Single(calls);
            }
        }

        /// <summary>The regression the CredentialSpec key exists to prevent.</summary>
        [Fact]
        public void A_user_and_a_service_principal_on_one_cloud_get_separate_credentials()
        {
            using (var dir = new TempDirectory())
            {
                var manager = DataverseConnectionManager.WithCredentials(
                    spec => new FakeCredential(spec.Cloud, spec.Kind), StoreIn(dir));

                var user = manager.GetCredential(Spec(kind: DataverseAuthKind.Interactive));
                var app = manager.GetCredential(Spec(kind: DataverseAuthKind.ClientSecret, principal: "secret-ref"));

                Assert.NotSame(user, app);
                Assert.Equal(DataverseAuthKind.ClientSecret, app.Kind);
            }
        }

        [Theory]
        [InlineData("app-2", "user@contoso.com")]
        [InlineData("app-1", "other@contoso.com")]
        public void Differing_app_or_user_gets_a_separate_credential(string clientId, string principal)
        {
            using (var dir = new TempDirectory())
            {
                var manager = DataverseConnectionManager.WithCredentials(
                    spec => new FakeCredential(spec.Cloud, spec.Kind), StoreIn(dir));

                Assert.NotSame(
                    manager.GetCredential(Spec()),
                    manager.GetCredential(Spec(clientId: clientId, principal: principal)));
            }
        }

        [Fact]
        public void Get_token_source_returns_the_credentials_own_source()
        {
            using (var dir = new TempDirectory())
            {
                var source = new FakeTokenSource();
                var manager = DataverseConnectionManager.WithCredentials(
                    spec => new FakeCredential(spec.Cloud, spec.Kind, source), StoreIn(dir));

                Assert.Same(source, manager.GetTokenSource(Spec()));
            }
        }

        /// <summary>On-premises kinds have no token; the failure must name the kind, not throw NRE.</summary>
        [Fact]
        public void Get_token_source_explains_itself_for_kinds_with_no_token()
        {
            using (var dir = new TempDirectory())
            {
                var manager = DataverseConnectionManager.WithCredentials(
                    spec => FakeCredential.WithoutToken(spec.Cloud), StoreIn(dir));

                var error = Assert.Throws<NotSupportedException>(
                    () => manager.GetTokenSource(Spec(kind: DataverseAuthKind.WindowsIntegrated)));

                Assert.Contains("WindowsIntegrated", error.Message);
            }
        }

        [Fact]
        public void A_factory_returning_null_fails_loudly()
        {
            using (var dir = new TempDirectory())
            {
                var manager = DataverseConnectionManager.WithCredentials(spec => null, StoreIn(dir));

                Assert.Throws<InvalidOperationException>(() => manager.GetCredential(Spec()));
            }
        }

        [Fact]
        public void Null_spec_is_rejected()
        {
            using (var dir = new TempDirectory())
            {
                var manager = DataverseConnectionManager.WithCredentials(
                    spec => new FakeCredential(), StoreIn(dir));

                Assert.Throws<ArgumentNullException>(() => manager.GetCredential(null));
            }
        }

        [Fact]
        public void The_default_manager_refuses_unimplemented_kinds_and_says_how_to_fix_it()
        {
            using (var dir = new TempDirectory())
            {
                var manager = new DataverseConnectionManager(
                    cloud => new DataverseAuthOptions { ClientId = "app-1" }, StoreIn(dir));

                var error = Assert.Throws<NotSupportedException>(
                    () => manager.GetCredential(Spec(kind: DataverseAuthKind.Certificate, principal: "thumb")));

                Assert.Contains("Certificate", error.Message);
                Assert.Contains(nameof(DataverseConnectionManager.WithCredentials), error.Message);
            }
        }

        [Fact]
        public void The_default_manager_now_builds_client_secret_connections()
        {
            using (var dir = new TempDirectory())
            {
                var secrets = new DpapiSecretStore(dir.Path);
                var reference = secrets.Write(null, "the-secret");

                var manager = new DataverseConnectionManager(
                    cloud => new DataverseAuthOptions { ClientId = "app-1", TenantId = "contoso.onmicrosoft.com" },
                    StoreIn(dir),
                    secrets);

                var credential = manager.GetCredential(
                    Spec(kind: DataverseAuthKind.ClientSecret, principal: reference));

                Assert.Equal(DataverseAuthKind.ClientSecret, credential.Kind);
                Assert.False(credential.TokenSource.IsInteractive);
                Assert.False(credential.TokenSource.SupportsGlobalDiscovery);
            }
        }

        /// <summary>A per-connection app registration must beat the host default.</summary>
        [Fact]
        public void Spec_identity_overrides_the_host_default_options()
        {
            using (var dir = new TempDirectory())
            {
                DataverseAuthOptions captured = null;

                var manager = new DataverseConnectionManager(
                    cloud =>
                    {
                        captured = new DataverseAuthOptions { ClientId = "host-default", TenantId = "host-tenant" };
                        return captured;
                    },
                    StoreIn(dir));

                manager.GetCredential(new CredentialSpec(
                    DataverseCloud.UsGovernmentHigh, DataverseAuthKind.Interactive,
                    "per-connection-app", "per-connection-tenant", "user@contoso.us"));

                Assert.Equal("per-connection-app", captured.ClientId);
                Assert.Equal("per-connection-tenant", captured.TenantId);
                Assert.Equal(DataverseCloud.UsGovernmentHigh, captured.Cloud);
                Assert.Equal("user@contoso.us", captured.Principal);
            }
        }

        [Fact]
        public void A_spec_without_identity_leaves_the_host_default_alone()
        {
            using (var dir = new TempDirectory())
            {
                DataverseAuthOptions captured = null;

                var manager = new DataverseConnectionManager(
                    cloud =>
                    {
                        captured = new DataverseAuthOptions { ClientId = "host-default", TenantId = "host-tenant" };
                        return captured;
                    },
                    StoreIn(dir));

                manager.GetCredential(CredentialSpec.ForDiscovery(DataverseCloud.Commercial));

                Assert.Equal("host-default", captured.ClientId);
                Assert.Equal("host-tenant", captured.TenantId);
            }
        }

        [Fact]
        public void Adding_a_connection_by_url_saves_an_interactive_profile()
        {
            using (var dir = new TempDirectory())
            {
                var manager = DataverseConnectionManager.WithCredentials(
                    spec => new FakeCredential(), StoreIn(dir));

                var profile = manager.Add(null, "contoso.crm.dynamics.com");

                Assert.Equal(DataverseAuthKind.Interactive, profile.AuthKind);
                Assert.Equal("https://contoso.crm.dynamics.com", profile.EnvironmentUrl);
                Assert.True(manager.AlreadySaved("HTTPS://CONTOSO.CRM.DYNAMICS.COM"));
                Assert.Single(manager.Profiles);
            }
        }

        [Fact]
        public void An_unparseable_url_is_rejected_before_anything_is_saved()
        {
            using (var dir = new TempDirectory())
            {
                var manager = DataverseConnectionManager.WithCredentials(
                    spec => new FakeCredential(), StoreIn(dir));

                Assert.Throws<FormatException>(() => manager.Add("bad", "not a url at all"));
                Assert.Empty(manager.Profiles);
            }
        }

        /// <summary>Deleting a connection must not orphan its secret.</summary>
        [Fact]
        public void Deleting_a_connection_deletes_its_secret()
        {
            using (var dir = new TempDirectory())
            {
                var secrets = new DpapiSecretStore(dir.Path);
                var manager = DataverseConnectionManager.WithCredentials(
                    spec => new FakeCredential(), StoreIn(dir), secrets);

                var profile = manager.Add(null, "contoso.crm.dynamics.com");
                profile.SecretRef = secrets.Write(null, "sup3r-s3cret-value");

                manager.Delete(profile);

                Assert.Null(secrets.Read(profile.SecretRef));
                Assert.Empty(manager.Profiles);
            }
        }

        [Fact]
        public void Deleting_a_connection_without_a_secret_is_harmless()
        {
            using (var dir = new TempDirectory())
            {
                var manager = DataverseConnectionManager.WithCredentials(
                    spec => new FakeCredential(), StoreIn(dir), new DpapiSecretStore(dir.Path));

                manager.Delete(manager.Add(null, "contoso.crm.dynamics.com"));
                manager.Delete(null);

                Assert.Empty(manager.Profiles);
            }
        }

        [Fact]
        public void Saved_connections_resolve_to_their_own_credential()
        {
            using (var dir = new TempDirectory())
            {
                var specs = new List<CredentialSpec>();
                var manager = DataverseConnectionManager.WithCredentials(
                    spec => { specs.Add(spec); return new FakeCredential(spec.Cloud, spec.Kind); },
                    StoreIn(dir));

                var profile = manager.Add(null, "contoso.crm.dynamics.com");
                profile.AuthKindName = "ClientSecret";
                profile.SecretRef = "ref-1";

                manager.GetCredential(profile.ToCredentialSpec());

                Assert.Equal(DataverseAuthKind.ClientSecret, specs.Single().Kind);
                Assert.Equal("ref-1", specs.Single().Principal);
            }
        }

        [Fact]
        public void Starts_disconnected_and_disconnecting_again_is_a_no_op()
        {
            using (var dir = new TempDirectory())
            {
                var manager = DataverseConnectionManager.WithCredentials(
                    spec => new FakeCredential(), StoreIn(dir));

                var changes = 0;
                manager.ConnectionChanged += (s, e) => changes++;

                manager.Disconnect();

                Assert.False(manager.IsConnected);
                Assert.Null(manager.Current);
                Assert.Null(manager.CurrentProfile);
                Assert.Equal(0, changes);
            }
        }

        [Fact]
        public void A_null_credential_factory_is_rejected()
        {
            Assert.Throws<ArgumentNullException>(() => DataverseConnectionManager.WithCredentials(null));
            Assert.Throws<ArgumentNullException>(() => new DataverseConnectionManager(null));
        }

        [Fact]
        public async Task Connecting_without_a_profile_is_rejected()
        {
            using (var dir = new TempDirectory())
            {
                var manager = DataverseConnectionManager.WithCredentials(
                    spec => new FakeCredential(), StoreIn(dir));

                await Assert.ThrowsAsync<ArgumentNullException>(() => manager.ConnectAsync(null));
            }
        }

        /// <summary>Clouds may not even share an identity authority, so a mismatch must not be attempted.</summary>
        [Fact]
        public async Task A_credential_from_the_wrong_cloud_is_refused_before_connecting()
        {
            using (var dir = new TempDirectory())
            {
                var manager = DataverseConnectionManager.WithCredentials(
                    spec => new FakeCredential(DataverseCloud.UsGovernmentHigh, spec.Kind), StoreIn(dir));

                var profile = manager.Add(null, "contoso.crm.dynamics.com");

                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ConnectAsync(profile));

                Assert.Contains(nameof(DataverseCloud.Commercial), error.Message);
                Assert.Contains(nameof(DataverseCloud.UsGovernmentHigh), error.Message);
            }
        }

        /// <summary>
        /// The new client is built before the old one is torn down, so a failed connect must
        /// leave a working connection — and the manager's own state — untouched.
        /// </summary>
        [Fact]
        public async Task A_failed_connect_changes_nothing_and_announces_nothing()
        {
            using (var dir = new TempDirectory())
            {
                var manager = DataverseConnectionManager.WithCredentials(
                    spec => new FakeCredential(), StoreIn(dir));

                var changes = 0;
                manager.ConnectionChanged += (s, e) => changes++;

                var profile = manager.Add(null, "contoso.crm.dynamics.com");

                await Assert.ThrowsAsync<NotSupportedException>(() => manager.ConnectAsync(profile));

                Assert.Null(manager.Current);
                Assert.Null(manager.CurrentProfile);
                Assert.False(manager.IsConnected);
                Assert.Equal(0, changes);
            }
        }

        /// <summary>A failed connect must not leave the profile half-renamed either.</summary>
        [Fact]
        public async Task A_failed_connect_leaves_the_placeholder_name_alone()
        {
            using (var dir = new TempDirectory())
            {
                var manager = DataverseConnectionManager.WithCredentials(
                    spec => new FakeCredential(), StoreIn(dir));

                var profile = manager.Add(null, "contoso.crm.dynamics.com");

                await Assert.ThrowsAsync<NotSupportedException>(() => manager.ConnectAsync(profile));

                Assert.True(profile.NameIsAuto);
                Assert.Equal("contoso", profile.Name);
            }
        }
    }
}

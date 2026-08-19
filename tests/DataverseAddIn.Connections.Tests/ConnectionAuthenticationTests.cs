using System;
using System.Linq;
using DataverseAddIn.Discovery;
using Xunit;

namespace DataverseAddIn.Connections.Tests
{
    /// <summary>
    /// The path a saved connection takes from the connection dialog: a kind, some identity
    /// fields, and a secret that must reach the secret store and nowhere else.
    /// </summary>
    public class ConnectionAuthenticationTests
    {
        private static ConnectionAuthentication ClientSecret(string secret = "the-secret") =>
            new ConnectionAuthentication
            {
                Kind = DataverseAuthKind.ClientSecret,
                ClientId = "app-1",
                TenantId = "contoso.onmicrosoft.com",
                ClientSecret = secret
            };

        [Fact]
        public void Adding_a_client_secret_connection_stores_the_secret_out_of_band()
        {
            using (var dir = new TempDirectory())
            {
                var secrets = new DpapiSecretStore(dir.Path);
                var manager = DataverseConnectionManager.WithCredentials(
                    spec => new FakeCredential(), new ConnectionStore(dir.File("connections.json")), secrets);

                var profile = manager.Add(null, "contoso.crm.dynamics.com", null, ClientSecret());

                Assert.Equal(DataverseAuthKind.ClientSecret, profile.AuthKind);
                Assert.Equal("app-1", profile.ClientId);
                Assert.Equal("contoso.onmicrosoft.com", profile.TenantId);
                Assert.NotNull(profile.SecretRef);
                Assert.Equal("the-secret", secrets.Read(profile.SecretRef));
            }
        }

        [Fact]
        public void The_secret_never_reaches_the_connections_file()
        {
            using (var dir = new TempDirectory())
            {
                var path = dir.File("connections.json");
                var manager = DataverseConnectionManager.WithCredentials(
                    spec => new FakeCredential(), new ConnectionStore(path), new DpapiSecretStore(dir.Path));

                manager.Add(null, "contoso.crm.dynamics.com", null, ClientSecret("sup3r-s3cret-value"));

                Assert.DoesNotContain("sup3r-s3cret-value", System.IO.File.ReadAllText(path));
            }
        }

        [Fact]
        public void Rotating_a_secret_reuses_the_reference()
        {
            using (var dir = new TempDirectory())
            {
                var secrets = new DpapiSecretStore(dir.Path);
                var manager = DataverseConnectionManager.WithCredentials(
                    spec => new FakeCredential(), new ConnectionStore(dir.File("connections.json")), secrets);

                var profile = manager.Add(null, "contoso.crm.dynamics.com", null, ClientSecret("first"));
                var reference = profile.SecretRef;

                manager.UpdateAuthentication(profile, ClientSecret("second"));

                Assert.Equal(reference, profile.SecretRef);
                Assert.Equal("second", secrets.Read(reference));
            }
        }

        /// <summary>A blank secret box on edit means "keep what is saved", not "clear it".</summary>
        [Fact]
        public void Editing_without_retyping_the_secret_keeps_it()
        {
            using (var dir = new TempDirectory())
            {
                var secrets = new DpapiSecretStore(dir.Path);
                var manager = DataverseConnectionManager.WithCredentials(
                    spec => new FakeCredential(), new ConnectionStore(dir.File("connections.json")), secrets);

                var profile = manager.Add(null, "contoso.crm.dynamics.com", null, ClientSecret("original"));

                manager.UpdateAuthentication(profile, ClientSecret(secret: null));

                Assert.NotNull(profile.SecretRef);
                Assert.Equal("original", secrets.Read(profile.SecretRef));
            }
        }

        /// <summary>Switching away from a secret kind must not leave a secret sitting at rest.</summary>
        [Fact]
        public void Switching_to_interactive_discards_the_secret()
        {
            using (var dir = new TempDirectory())
            {
                var secrets = new DpapiSecretStore(dir.Path);
                var manager = DataverseConnectionManager.WithCredentials(
                    spec => new FakeCredential(), new ConnectionStore(dir.File("connections.json")), secrets);

                var profile = manager.Add(null, "contoso.crm.dynamics.com", null, ClientSecret());
                var reference = profile.SecretRef;

                manager.UpdateAuthentication(profile, new ConnectionAuthentication
                {
                    Kind = DataverseAuthKind.Interactive,
                    UserName = "user@contoso.com"
                });

                Assert.Equal(DataverseAuthKind.Interactive, profile.AuthKind);
                Assert.Null(profile.SecretRef);
                Assert.Null(secrets.Read(reference));
                Assert.Empty(System.IO.Directory.GetFiles(dir.Path, "*.bin"));
            }
        }

        [Fact]
        public void Authentication_survives_a_save_and_reload()
        {
            using (var dir = new TempDirectory())
            {
                var path = dir.File("connections.json");
                var manager = DataverseConnectionManager.WithCredentials(
                    spec => new FakeCredential(), new ConnectionStore(path), new DpapiSecretStore(dir.Path));

                var saved = manager.Add("Contoso", "contoso.crm.dynamics.com", null, ClientSecret());

                var reloaded = new ConnectionStore(path).Profiles.Single();

                Assert.Equal(DataverseAuthKind.ClientSecret, reloaded.AuthKind);
                Assert.Equal(saved.SecretRef, reloaded.SecretRef);
                Assert.Equal(saved.ToCredentialSpec(), reloaded.ToCredentialSpec());
            }
        }

        [Fact]
        public void Omitting_authentication_leaves_a_connection_interactive()
        {
            using (var dir = new TempDirectory())
            {
                var manager = DataverseConnectionManager.WithCredentials(
                    spec => new FakeCredential(), new ConnectionStore(dir.File("connections.json")));

                var profile = manager.Add(null, "contoso.crm.dynamics.com");

                Assert.Equal(DataverseAuthKind.Interactive, profile.AuthKind);
                Assert.Null(profile.SecretRef);
            }
        }

        [Fact]
        public void Reading_back_a_profile_recovers_everything_except_the_secret()
        {
            var profile = new ConnectionProfile
            {
                CloudName = "Commercial",
                AuthKindName = "ClientSecret",
                ClientId = "app-1",
                TenantId = "tenant-1",
                UserName = "user@contoso.com",
                CertificateThumbprint = "DC6C689022C905EA",
                SecretRef = "ref-1"
            };

            var authentication = ConnectionAuthentication.FromProfile(profile);

            Assert.Equal(DataverseAuthKind.ClientSecret, authentication.Kind);
            Assert.Equal("app-1", authentication.ClientId);
            Assert.Equal("tenant-1", authentication.TenantId);
            Assert.Equal("user@contoso.com", authentication.UserName);
            Assert.Equal("DC6C689022C905EA", authentication.CertificateThumbprint);
            Assert.Null(authentication.ClientSecret);
        }

        [Fact]
        public void Null_profile_is_rejected()
        {
            Assert.Throws<ArgumentNullException>(() => ConnectionAuthentication.FromProfile(null));
        }
    }
}

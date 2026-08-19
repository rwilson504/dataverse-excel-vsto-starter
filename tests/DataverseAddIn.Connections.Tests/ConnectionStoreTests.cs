using System.IO;
using System.Linq;
using DataverseAddIn.Discovery;
using Xunit;

namespace DataverseAddIn.Connections.Tests
{
    public class ConnectionStoreTests
    {
        private static ConnectionProfile Sample() => new ConnectionProfile
        {
            Name = "Contoso",
            EnvironmentUrl = "https://contoso.crm.dynamics.com",
            CloudName = "Commercial",
            AuthKindName = "ClientSecret",
            ClientId = "app-1",
            TenantId = "tenant-1",
            UserName = "user@contoso.com",
            CertificateThumbprint = "DC6C689022C905EA",
            CertificateStoreLocation = "CurrentUser",
            CertificateStoreName = "My",
            SecretRef = "abc123",
            Color = "#C62828"
        };

        [Fact]
        public void Round_trips_every_authentication_field()
        {
            using (var dir = new TempDirectory())
            {
                var path = dir.File("connections.json");
                new ConnectionStore(path).Add(Sample());

                var loaded = Assert.Single(new ConnectionStore(path).Profiles);

                Assert.Equal(DataverseAuthKind.ClientSecret, loaded.AuthKind);
                Assert.Equal("app-1", loaded.ClientId);
                Assert.Equal("tenant-1", loaded.TenantId);
                Assert.Equal("user@contoso.com", loaded.UserName);
                Assert.Equal("DC6C689022C905EA", loaded.CertificateThumbprint);
                Assert.Equal("CurrentUser", loaded.CertificateStoreLocation);
                Assert.Equal("My", loaded.CertificateStoreName);
                Assert.Equal("abc123", loaded.SecretRef);
            }
        }

        /// <summary>
        /// The file is plain JSON in the roaming profile. A secret must never reach it, so this
        /// fails the moment anyone adds a property that carries one.
        /// </summary>
        [Fact]
        public void Persists_only_a_secret_reference_never_a_secret()
        {
            using (var dir = new TempDirectory())
            {
                var path = dir.File("connections.json");

                var profile = Sample();
                profile.SecretRef = new DpapiSecretStore(dir.Path).Write(null, "sup3r-s3cret-value");

                new ConnectionStore(path).Add(profile);

                var json = File.ReadAllText(path);

                Assert.DoesNotContain("sup3r-s3cret-value", json);
                Assert.Contains(profile.SecretRef, json);
            }
        }

        [Fact]
        public void Remove_deletes_by_id_and_persists()
        {
            using (var dir = new TempDirectory())
            {
                var path = dir.File("connections.json");
                var store = new ConnectionStore(path);

                var profile = Sample();
                store.Add(profile);
                store.Add(new ConnectionProfile { Name = "Other", EnvironmentUrl = "https://other.crm.dynamics.com" });
                store.Remove(profile);

                Assert.Equal("Other", Assert.Single(new ConnectionStore(path).Profiles).Name);
            }
        }

        [Fact]
        public void Contains_url_ignores_case()
        {
            using (var dir = new TempDirectory())
            {
                var store = new ConnectionStore(dir.File("connections.json"));
                store.Add(Sample());

                Assert.True(store.ContainsUrl("HTTPS://CONTOSO.CRM.DYNAMICS.COM"));
                Assert.False(store.ContainsUrl("https://other.crm.dynamics.com"));
            }
        }

        /// <summary>A half-written file must not stop the add-in from loading.</summary>
        [Fact]
        public void Corrupt_file_loads_as_empty()
        {
            using (var dir = new TempDirectory())
            {
                var path = dir.File("connections.json");
                File.WriteAllText(path, "{ this is not json");

                Assert.Empty(new ConnectionStore(path).Profiles);
            }
        }

        [Fact]
        public void Missing_file_loads_as_empty()
        {
            using (var dir = new TempDirectory())
                Assert.Empty(new ConnectionStore(dir.File("does-not-exist.json")).Profiles);
        }

        [Fact]
        public void Profiles_get_distinct_ids()
        {
            using (var dir = new TempDirectory())
            {
                var store = new ConnectionStore(dir.File("connections.json"));
                store.Add(Sample());
                store.Add(Sample());

                Assert.Equal(2, store.Profiles.Select(p => p.Id).Distinct().Count());
            }
        }
    }
}

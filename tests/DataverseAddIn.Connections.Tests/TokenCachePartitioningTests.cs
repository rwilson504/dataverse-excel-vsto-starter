using System.IO;
using System.Linq;
using DataverseAddIn.Discovery;
using Xunit;

namespace DataverseAddIn.Connections.Tests
{
    /// <summary>
    /// The token cache file name is the second half of the credential key. If two identities
    /// resolve to one file they overwrite each other's cache, because MSAL serializes the whole
    /// cache on every write.
    /// </summary>
    public class TokenCachePartitioningTests
    {
        private static DataverseAuthOptions Options(
            DataverseCloud cloud = DataverseCloud.Commercial,
            DataverseAuthKind kind = DataverseAuthKind.Interactive,
            string clientId = "app-1",
            string tenantId = "tenant-1",
            string principal = "user@contoso.com") =>
            new DataverseAuthOptions
            {
                Cloud = cloud,
                AuthKind = kind,
                ClientId = clientId,
                TenantId = tenantId,
                Principal = principal
            };

        private static string FileName(DataverseAuthOptions options) =>
            Path.GetFileName(options.ResolveTokenCacheFilePath());

        [Fact]
        public void The_same_identity_always_resolves_to_the_same_file()
        {
            Assert.Equal(FileName(Options()), FileName(Options()));
        }

        [Theory]
        [InlineData(DataverseCloud.UsGovernmentHigh, DataverseAuthKind.Interactive, "app-1", "tenant-1", "user@contoso.com")]
        [InlineData(DataverseCloud.Commercial, DataverseAuthKind.ClientSecret, "app-1", "tenant-1", "user@contoso.com")]
        [InlineData(DataverseCloud.Commercial, DataverseAuthKind.Interactive, "app-2", "tenant-1", "user@contoso.com")]
        [InlineData(DataverseCloud.Commercial, DataverseAuthKind.Interactive, "app-1", "tenant-2", "user@contoso.com")]
        [InlineData(DataverseCloud.Commercial, DataverseAuthKind.Interactive, "app-1", "tenant-1", "other@contoso.com")]
        [InlineData(DataverseCloud.Commercial, DataverseAuthKind.Interactive, "app-1", "tenant-1", null)]
        public void Any_differing_field_resolves_to_a_different_file(
            DataverseCloud cloud, DataverseAuthKind kind, string clientId, string tenantId, string principal)
        {
            Assert.NotEqual(FileName(Options()), FileName(Options(cloud, kind, clientId, tenantId, principal)));
        }

        /// <summary>
        /// The regression this replaced: cloud plus client ID was the whole key, so a service
        /// principal and a signed-in user shared one cache file.
        /// </summary>
        [Fact]
        public void A_user_and_a_service_principal_on_one_app_do_not_share_a_cache()
        {
            var user = Options(kind: DataverseAuthKind.Interactive, principal: "user@contoso.com");
            var app = Options(kind: DataverseAuthKind.ClientSecret, principal: "secret-ref-1");

            Assert.NotEqual(FileName(user), FileName(app));
        }

        [Fact]
        public void Government_and_commercial_land_under_their_own_authority_host()
        {
            Assert.StartsWith("login.microsoftonline.com.", FileName(Options(DataverseCloud.Commercial)));
            Assert.StartsWith("login.microsoftonline.us.", FileName(Options(DataverseCloud.UsGovernmentHigh)));
        }

        /// <summary>Hashed so the file name stays short and carries no user name.</summary>
        [Fact]
        public void The_file_name_does_not_leak_the_principal()
        {
            Assert.DoesNotContain("user@contoso.com", FileName(Options()));
        }

        [Fact]
        public void An_explicit_path_wins()
        {
            var options = Options();
            options.TokenCacheFilePath = @"C:\somewhere\custom.msalcache";

            Assert.Equal(@"C:\somewhere\custom.msalcache", options.ResolveTokenCacheFilePath());
        }

        [Fact]
        public void Every_supported_kind_gets_its_own_file()
        {
            var names = AuthKindDescriptor.Supported
                .Select(d => FileName(Options(kind: d.Kind)))
                .ToList();

            Assert.Equal(names.Count, names.Distinct().Count());
        }
    }
}

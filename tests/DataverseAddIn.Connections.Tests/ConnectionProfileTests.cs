using DataverseAddIn.Discovery;
using Xunit;

namespace DataverseAddIn.Connections.Tests
{
    public class ConnectionProfileTests
    {
        private static DataverseEnvironmentReference Environment(string url = "https://contoso.crm.dynamics.com") =>
            DataverseEnvironmentReference.Parse(url);

        [Fact]
        public void Create_records_the_authentication_kind_and_cloud()
        {
            var profile = ConnectionProfile.Create("Contoso", Environment(), authKind: DataverseAuthKind.ClientSecret);

            Assert.Equal("ClientSecret", profile.AuthKindName);
            Assert.Equal(DataverseAuthKind.ClientSecret, profile.AuthKind);
            Assert.Equal(DataverseCloud.Commercial, profile.Cloud);
            Assert.False(profile.NameIsAuto);
        }

        [Fact]
        public void Create_defaults_to_interactive_and_auto_names_from_the_host()
        {
            var profile = ConnectionProfile.Create(null, Environment());

            Assert.Equal(DataverseAuthKind.Interactive, profile.AuthKind);
            Assert.True(profile.NameIsAuto);
            Assert.Equal("contoso", profile.Name);
        }

        /// <summary>Profiles saved before the field existed must keep working.</summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("SomethingRemoved")]
        public void Unknown_or_missing_kind_reads_as_interactive(string stored)
        {
            var profile = new ConnectionProfile { AuthKindName = stored };

            Assert.Equal(DataverseAuthKind.Interactive, profile.AuthKind);
        }

        [Fact]
        public void Unknown_or_missing_cloud_reads_as_commercial()
        {
            Assert.Equal(DataverseCloud.Commercial, new ConnectionProfile { CloudName = "Martian" }.Cloud);
        }

        [Fact]
        public void Certificate_connections_are_identified_by_thumbprint()
        {
            var profile = new ConnectionProfile
            {
                CloudName = "Commercial",
                AuthKindName = "Certificate",
                ClientId = "app-1",
                CertificateThumbprint = "DC6C689022C905EA",
                UserName = "ignored@contoso.com",
                SecretRef = "ignored"
            };

            Assert.Equal("DC6C689022C905EA", profile.ToCredentialSpec().Principal);
        }

        [Fact]
        public void Client_secret_connections_are_identified_by_secret_reference()
        {
            var profile = new ConnectionProfile
            {
                CloudName = "Commercial",
                AuthKindName = "ClientSecret",
                SecretRef = "abc123",
                UserName = "ignored@contoso.com"
            };

            Assert.Equal("abc123", profile.ToCredentialSpec().Principal);
        }

        [Fact]
        public void Everything_else_is_identified_by_user_name()
        {
            var profile = new ConnectionProfile
            {
                CloudName = "UsGovernmentHigh",
                AuthKindName = "Interactive",
                ClientId = "app-1",
                TenantId = "tenant-1",
                UserName = "user@contoso.us"
            };

            var spec = profile.ToCredentialSpec();

            Assert.Equal(DataverseCloud.UsGovernmentHigh, spec.Cloud);
            Assert.Equal(DataverseAuthKind.Interactive, spec.Kind);
            Assert.Equal("app-1", spec.ClientId);
            Assert.Equal("tenant-1", spec.TenantId);
            Assert.Equal("user@contoso.us", spec.Principal);
        }

        /// <summary>Two connections to one environment under different identities must not share a credential.</summary>
        [Fact]
        public void Same_environment_under_two_identities_yields_two_specs()
        {
            var user = new ConnectionProfile { CloudName = "Commercial", AuthKindName = "Interactive", UserName = "a@contoso.com" };
            var app = new ConnectionProfile { CloudName = "Commercial", AuthKindName = "ClientSecret", SecretRef = "ref-1" };

            Assert.NotEqual(user.ToCredentialSpec(), app.ToCredentialSpec());
        }

        [Fact]
        public void A_placeholder_name_gives_way_to_the_organization_name()
        {
            var profile = ConnectionProfile.Create(null, Environment());

            Assert.True(profile.AdoptOrganizationName("Contoso Production"));
            Assert.Equal("Contoso Production", profile.Name);
            Assert.False(profile.NameIsAuto);
        }

        /// <summary>The name a user typed is theirs; connecting must never overwrite it.</summary>
        [Fact]
        public void A_user_chosen_name_is_never_overwritten()
        {
            var profile = ConnectionProfile.Create("My name for it", Environment());

            Assert.False(profile.AdoptOrganizationName("Contoso Production"));
            Assert.Equal("My name for it", profile.Name);
        }

        [Fact]
        public void Adopting_happens_once_so_a_later_rename_sticks()
        {
            var profile = ConnectionProfile.Create(null, Environment());
            profile.AdoptOrganizationName("Contoso Production");

            profile.Name = "Renamed by me";

            Assert.False(profile.AdoptOrganizationName("Contoso Production"));
            Assert.Equal("Renamed by me", profile.Name);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void An_organization_with_no_usable_name_leaves_the_placeholder(string friendlyName)
        {
            var profile = ConnectionProfile.Create(null, Environment());

            Assert.False(profile.AdoptOrganizationName(friendlyName));
            Assert.Equal("contoso", profile.Name);
            Assert.True(profile.NameIsAuto);
        }

        [Fact]
        public void The_adopted_name_is_trimmed()
        {
            var profile = ConnectionProfile.Create(null, Environment());
            profile.AdoptOrganizationName("  Contoso Production  ");

            Assert.Equal("Contoso Production", profile.Name);
        }
    }
}

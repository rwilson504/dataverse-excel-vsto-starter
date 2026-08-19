using System;
using DataverseAddIn.Discovery;
using Xunit;

namespace DataverseAddIn.Connections.Tests
{
    public class ClientSecretTokenSourceTests
    {
        private static DataverseAuthOptions Options(
            string tenantId = "contoso.onmicrosoft.com",
            DataverseCloud cloud = DataverseCloud.Commercial) =>
            new DataverseAuthOptions
            {
                ClientId = "00001111-aaaa-2222-bbbb-3333cccc4444",
                TenantId = tenantId,
                Cloud = cloud,
                AuthKind = DataverseAuthKind.ClientSecret,
                Principal = "secret-ref"
            };

        [Fact]
        public void Is_non_interactive_and_cannot_list_environments()
        {
            var source = new ClientSecretTokenSource(Options(), "the-secret");

            Assert.False(source.IsInteractive);
            Assert.False(source.SupportsGlobalDiscovery);
            Assert.Equal(DataverseCloud.Commercial, source.Cloud);
        }

        /// <summary>
        /// The client credentials flow issues a token for one directory, so the multi-tenant
        /// aliases a public client happily uses are a configuration error, not a runtime one.
        /// </summary>
        [Theory]
        [InlineData("organizations")]
        [InlineData("common")]
        [InlineData("consumers")]
        [InlineData("ORGANIZATIONS")]
        public void Multi_tenant_authorities_are_rejected_up_front(string tenantId)
        {
            var error = Assert.Throws<InvalidOperationException>(
                () => new ClientSecretTokenSource(Options(tenantId), "the-secret"));

            Assert.Contains("specific tenant", error.Message);
        }

        [Fact]
        public void A_specific_tenant_is_accepted_in_either_form()
        {
            new ClientSecretTokenSource(Options("contoso.onmicrosoft.com"), "the-secret");
            new ClientSecretTokenSource(Options("aaaabbbb-0000-cccc-1111-dddd2222eeee"), "the-secret");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void A_missing_secret_is_rejected(string secret)
        {
            Assert.Throws<ArgumentException>(() => new ClientSecretTokenSource(Options(), secret));
        }

        [Fact]
        public void Missing_options_are_rejected()
        {
            Assert.Throws<ArgumentNullException>(() => new ClientSecretTokenSource(null, "the-secret"));

            var noClientId = Options();
            noClientId.ClientId = null;
            Assert.Throws<InvalidOperationException>(() => new ClientSecretTokenSource(noClientId, "the-secret"));
        }

        [Fact]
        public void Signing_out_is_a_no_op_because_there_is_no_user()
        {
            Assert.True(new ClientSecretTokenSource(Options(), "the-secret").SignOutAsync().IsCompleted);
        }

        /// <summary>Government clouds authenticate against a separate directory.</summary>
        [Fact]
        public void Works_against_a_government_cloud()
        {
            var source = new ClientSecretTokenSource(Options(cloud: DataverseCloud.UsGovernmentHigh), "the-secret");

            Assert.Equal(DataverseCloud.UsGovernmentHigh, source.Cloud);
        }
    }
}

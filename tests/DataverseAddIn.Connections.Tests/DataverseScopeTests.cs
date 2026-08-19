using System;
using DataverseAddIn.Discovery;
using Xunit;

namespace DataverseAddIn.Connections.Tests
{
    /// <summary>
    /// Dataverse hands back environment URLs with paths and trailing slashes, and Microsoft
    /// Entra ID rejects a scope built from either. Step 4 adds an application-scope variant
    /// here; these pin the delegated one first.
    /// </summary>
    public class DataverseScopeTests
    {
        [Theory]
        [InlineData("https://contoso.crm.dynamics.com")]
        [InlineData("https://contoso.crm.dynamics.com/")]
        [InlineData("https://contoso.crm.dynamics.com/api/data/v9.2")]
        [InlineData("https://contoso.crm.dynamics.com/api/data/v9.2/WhoAmI?$select=x")]
        public void Strips_everything_after_the_authority(string url)
        {
            Assert.Equal("https://contoso.crm.dynamics.com/user_impersonation", DataverseScope.Delegated(url));
        }

        [Fact]
        public void Keeps_a_non_default_port()
        {
            Assert.Equal(
                "https://crm.contoso.local:444/user_impersonation",
                DataverseScope.Delegated("https://crm.contoso.local:444/main.aspx"));
        }

        [Theory]
        [InlineData("https://globaldisco.crm.dynamics.com")]
        [InlineData("https://globaldisco.crm.microsoftdynamics.us")]
        [InlineData("https://globaldisco.crm.dynamics.cn")]
        public void Works_for_every_clouds_discovery_root(string discoveryRoot)
        {
            Assert.Equal(discoveryRoot + "/user_impersonation", DataverseScope.Delegated(discoveryRoot));
        }

        [Fact]
        public void Every_cloud_discovery_url_produces_a_usable_scope()
        {
            foreach (DataverseCloud cloud in Enum.GetValues(typeof(DataverseCloud)))
            {
                var scope = DataverseScope.Delegated(cloud.GetGlobalDiscoveryUrl());

                Assert.EndsWith("/user_impersonation", scope);
                Assert.DoesNotContain("//user_impersonation", scope);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("contoso.crm.dynamics.com")]
        [InlineData("not a url")]
        public void Rejects_anything_that_is_not_an_absolute_url(string url)
        {
            Assert.Throws<ArgumentException>(() => DataverseScope.Delegated(url));
        }

        [Fact]
        public void Resource_is_the_audience_without_the_scope_suffix()
        {
            Assert.Equal(
                "https://contoso.crm.dynamics.com",
                DataverseScope.Resource("https://contoso.crm.dynamics.com/api/data/v9.2/"));
        }

        /// <summary>
        /// Microsoft's guidance is explicit: public clients use <c>/user_impersonation</c>,
        /// confidential clients use <c>/.default</c>. Sending the wrong one fails at Entra ID.
        /// </summary>
        [Theory]
        [InlineData("https://contoso.crm.dynamics.com")]
        [InlineData("https://contoso.crm.dynamics.com/")]
        [InlineData("https://contoso.crm.dynamics.com/api/data/v9.2")]
        public void Confidential_clients_get_the_default_scope(string url)
        {
            Assert.Equal("https://contoso.crm.dynamics.com/.default", DataverseScope.Application(url));
        }

        [Fact]
        public void The_two_scopes_are_never_the_same()
        {
            const string url = "https://contoso.crm.dynamics.com";

            Assert.NotEqual(DataverseScope.Delegated(url), DataverseScope.Application(url));
            Assert.EndsWith("/user_impersonation", DataverseScope.Delegated(url));
            Assert.EndsWith("/.default", DataverseScope.Application(url));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("contoso.crm.dynamics.com")]
        public void The_application_scope_validates_its_input_too(string url)
        {
            Assert.Throws<ArgumentException>(() => DataverseScope.Application(url));
        }
    }
}

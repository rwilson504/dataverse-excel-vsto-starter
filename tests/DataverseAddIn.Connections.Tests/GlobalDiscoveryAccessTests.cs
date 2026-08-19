using System;
using System.Linq;
using System.Threading.Tasks;
using DataverseAddIn.Discovery;
using Xunit;

namespace DataverseAddIn.Connections.Tests
{
    /// <summary>
    /// Global Discovery is unavailable to on-premises deployments and to most service
    /// principals. The refusal has to happen up front, not as an opaque 401 later.
    /// </summary>
    public class GlobalDiscoveryAccessTests
    {
        [Fact]
        public void A_credential_without_discovery_is_refused_at_construction()
        {
            var source = new FakeTokenSource(supportsGlobalDiscovery: false);

            var error = Assert.Throws<ArgumentException>(() => new GlobalDiscoveryClient(source));

            Assert.Contains("environment URL", error.Message);
        }

        [Fact]
        public void A_credential_with_discovery_is_accepted()
        {
            using (new GlobalDiscoveryClient(new FakeTokenSource()))
            {
            }
        }

        [Fact]
        public async Task A_null_token_source_is_rejected()
        {
            Assert.Throws<ArgumentNullException>(() => new GlobalDiscoveryClient(null));

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => ((IDataverseTokenSource)null).GetDiscoveryTokenAsync());
        }

        [Fact]
        public async Task Discovery_tokens_are_requested_for_the_sources_own_cloud()
        {
            foreach (DataverseCloud cloud in Enum.GetValues(typeof(DataverseCloud)))
            {
                var source = new FakeTokenSource(cloud);

                Assert.Equal("fake-token", await source.GetDiscoveryTokenAsync());
                Assert.Equal(cloud.GetGlobalDiscoveryUrl(), source.RequestedResources.Single());
            }
        }

        [Fact]
        public async Task Asking_for_a_discovery_token_without_discovery_names_the_cloud()
        {
            var source = new FakeTokenSource(DataverseCloud.UsGovernmentHigh, supportsGlobalDiscovery: false);

            var error = await Assert.ThrowsAsync<NotSupportedException>(() => source.GetDiscoveryTokenAsync());

            Assert.Contains(nameof(DataverseCloud.UsGovernmentHigh), error.Message);
            Assert.Empty(source.RequestedResources);
        }
    }
}

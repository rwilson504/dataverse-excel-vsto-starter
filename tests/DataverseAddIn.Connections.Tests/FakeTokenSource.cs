using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataverseAddIn.Discovery;

namespace DataverseAddIn.Connections.Tests
{
    /// <summary>Records what was asked for and hands back a canned token. Never leaves the process.</summary>
    internal sealed class FakeTokenSource : IDataverseTokenSource
    {
        public FakeTokenSource(
            DataverseCloud cloud = DataverseCloud.Commercial,
            bool isInteractive = true,
            bool supportsGlobalDiscovery = true)
        {
            Cloud = cloud;
            IsInteractive = isInteractive;
            SupportsGlobalDiscovery = supportsGlobalDiscovery;
        }

        public DataverseCloud Cloud { get; }

        public bool IsInteractive { get; }

        public bool SupportsGlobalDiscovery { get; }

        public List<string> RequestedResources { get; } = new List<string>();

        public int SignOutCount { get; private set; }

        public Task<string> GetTokenAsync(string resourceUrl, CancellationToken cancellationToken = default)
        {
            RequestedResources.Add(resourceUrl);
            return Task.FromResult("fake-token");
        }

        public Task SignOutAsync()
        {
            SignOutCount++;
            return Task.FromResult(0);
        }
    }
}

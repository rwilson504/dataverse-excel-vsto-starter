using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DataverseAddIn.Discovery
{
    /// <summary>
    /// Runs Global Discovery across several clouds and merges the results.
    /// </summary>
    /// <remarks>
    /// Each cloud has its own discovery endpoint and its own token, so this is one call per
    /// cloud regardless of identity. Commercial and GCC share public Microsoft Entra ID, so a
    /// single sign-in covers both. GCC High and DoD authenticate against Microsoft Entra
    /// Government and need their own app registration and their own sign-in.
    /// <para>
    /// Clouds are queried sequentially rather than in parallel: the first cloud on an authority
    /// may need an interactive prompt, and firing several sign-in windows at once is hostile
    /// inside Excel. Once a cloud's authority has a cached account, its siblings resolve silently.
    /// </para>
    /// </remarks>
    public sealed class MultiCloudDiscoveryClient : IDisposable
    {
        private readonly IReadOnlyList<IDataverseTokenSource> _tokenSources;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;

        public MultiCloudDiscoveryClient(IEnumerable<IDataverseTokenSource> tokenSources, HttpClient httpClient = null)
        {
            if (tokenSources == null) throw new ArgumentNullException(nameof(tokenSources));

            _tokenSources = tokenSources.ToList();

            if (_tokenSources.Count == 0)
                throw new ArgumentException("At least one token source is required.", nameof(tokenSources));

            var duplicate = _tokenSources.GroupBy(a => a.Cloud).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
                throw new ArgumentException($"More than one token source was supplied for {duplicate.Key}.", nameof(tokenSources));

            _ownsHttpClient = httpClient == null;
            _httpClient = httpClient ?? new HttpClient();
        }

        /// <summary>
        /// Queries every configured cloud. A cloud that fails (no account in that identity
        /// cloud, consent not granted, network blocked) is reported in
        /// <see cref="MultiCloudDiscoveryResult.Failures"/> rather than failing the whole call.
        /// </summary>
        public async Task<MultiCloudDiscoveryResult> GetInstancesAsync(
            string filter = null,
            IEnumerable<string> select = null,
            CancellationToken cancellationToken = default)
        {
            var selected = select?.ToList();
            var instances = new List<DataverseInstance>();
            var failures = new List<CloudDiscoveryFailure>();

            foreach (var tokenSource in _tokenSources)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using (var client = new GlobalDiscoveryClient(tokenSource, _httpClient))
                    {
                        var found = await client.GetInstancesAsync(filter, selected, cancellationToken).ConfigureAwait(false);
                        instances.AddRange(found);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failures.Add(new CloudDiscoveryFailure(tokenSource.Cloud, ex));
                }
            }

            return new MultiCloudDiscoveryResult(instances, failures);
        }

        public void Dispose()
        {
            if (_ownsHttpClient) _httpClient.Dispose();
        }
    }

    public sealed class MultiCloudDiscoveryResult
    {
        internal MultiCloudDiscoveryResult(IReadOnlyList<DataverseInstance> instances, IReadOnlyList<CloudDiscoveryFailure> failures)
        {
            Instances = instances;
            Failures = failures;
        }

        public IReadOnlyList<DataverseInstance> Instances { get; }

        /// <summary>Clouds that could not be queried. Usually safe to surface as a warning.</summary>
        public IReadOnlyList<CloudDiscoveryFailure> Failures { get; }
    }

    public sealed class CloudDiscoveryFailure
    {
        internal CloudDiscoveryFailure(DataverseCloud cloud, Exception error)
        {
            Cloud = cloud;
            Error = error;
        }

        public DataverseCloud Cloud { get; }

        public Exception Error { get; }
    }
}

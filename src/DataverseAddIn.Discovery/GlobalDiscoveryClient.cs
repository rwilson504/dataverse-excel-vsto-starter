using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DataverseAddIn.Discovery
{
    /// <summary>
    /// Reads the Global Discovery Service OData endpoint to list the environments the
    /// signed-in user can access.
    /// </summary>
    public sealed class GlobalDiscoveryClient : IDisposable
    {
        private const string InstancesPath = "/api/discovery/v2.0/Instances";

        private static readonly string[] DefaultSelect =
        {
            "ApiUrl", "Url", "FriendlyName", "UniqueName", "UrlName", "Id",
            "EnvironmentId", "TenantId", "Version", "Region", "State",
            "OrganizationType", "IsUserSysAdmin", "Purpose"
        };

        private readonly IDataverseTokenSource _tokenSource;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;

        public GlobalDiscoveryClient(IDataverseTokenSource tokenSource, HttpClient httpClient = null)
        {
            _tokenSource = tokenSource ?? throw new ArgumentNullException(nameof(tokenSource));

            if (!_tokenSource.SupportsGlobalDiscovery)
            {
                throw new ArgumentException(
                    "This credential cannot call the Global Discovery Service. Ask the user for an " +
                    "environment URL instead.", nameof(tokenSource));
            }

            // .NET Framework 4.6.2 honours the OS default, but older policy settings on managed
            // machines can still leave TLS 1.0 selected, which Dataverse rejects outright.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            _ownsHttpClient = httpClient == null;
            _httpClient = httpClient ?? new HttpClient();
        }

        /// <summary>
        /// Lists environments. Both query options are optional; the service supports the OData
        /// <c>$select</c> and <c>$filter</c> subset documented for the Instance entity.
        /// </summary>
        /// <param name="filter">
        /// Raw OData filter, e.g. <c>State eq 0</c> or <c>IsUserSysAdmin eq true</c>.
        /// Note that Global Discovery string comparisons are case sensitive.
        /// </param>
        public async Task<IReadOnlyList<DataverseInstance>> GetInstancesAsync(
            string filter = null,
            IEnumerable<string> select = null,
            CancellationToken cancellationToken = default)
        {
            var token = await _tokenSource.GetDiscoveryTokenAsync(cancellationToken).ConfigureAwait(false);

            var requestUri = BuildRequestUri(_tokenSource.Cloud.GetGlobalDiscoveryUrl(), filter, select);

            using (var request = new HttpRequestMessage(HttpMethod.Get, requestUri))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Add("OData-MaxVersion", "4.0");
                request.Headers.Add("OData-Version", "4.0");

                using (var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                        throw new GlobalDiscoveryException(response.StatusCode, body);

                    var instances = Deserialize(body);

                    foreach (var instance in instances)
                        instance.Cloud = _tokenSource.Cloud;

                    return instances;
                }
            }
        }

        internal static string BuildRequestUri(string discoveryRoot, string filter, IEnumerable<string> select)
        {
            var selected = select == null ? DefaultSelect : select.ToArray();

            var query = new StringBuilder();
            if (selected.Length > 0)
                query.Append("$select=").Append(Uri.EscapeDataString(string.Join(",", selected)));

            if (!string.IsNullOrWhiteSpace(filter))
            {
                if (query.Length > 0) query.Append('&');
                query.Append("$filter=").Append(Uri.EscapeDataString(filter));
            }

            var url = discoveryRoot.TrimEnd('/') + InstancesPath;
            return query.Length > 0 ? url + "?" + query : url;
        }

        private static List<DataverseInstance> Deserialize(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(DataverseInstanceCollection));

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var collection = (DataverseInstanceCollection)serializer.ReadObject(stream);
                return collection?.Value ?? new List<DataverseInstance>();
            }
        }

        public void Dispose()
        {
            if (_ownsHttpClient) _httpClient.Dispose();
        }
    }

    public sealed class GlobalDiscoveryException : Exception
    {
        public GlobalDiscoveryException(HttpStatusCode statusCode, string responseBody)
            : base($"Global Discovery Service returned {(int)statusCode} {statusCode}. {responseBody}")
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
        }

        public HttpStatusCode StatusCode { get; }

        public string ResponseBody { get; }
    }
}

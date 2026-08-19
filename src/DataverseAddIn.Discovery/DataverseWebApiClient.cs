using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DataverseAddIn.Discovery
{
    /// <summary>
    /// Minimal Dataverse Web API caller, included to prove that the per-environment token
    /// acquired after discovery actually works. Replace with ServiceClient or your own
    /// client once the add-in needs real data operations.
    /// </summary>
    public sealed class DataverseWebApiClient : IDisposable
    {
        private const string ApiVersionPath = "/api/data/v9.2";

        private readonly IDataverseTokenSource _tokenSource;
        private readonly string _environmentUrl;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;

        public DataverseWebApiClient(IDataverseTokenSource tokenSource, string environmentUrl, HttpClient httpClient = null)
        {
            _tokenSource = tokenSource ?? throw new ArgumentNullException(nameof(tokenSource));

            if (string.IsNullOrWhiteSpace(environmentUrl))
                throw new ArgumentException("Environment URL is required.", nameof(environmentUrl));

            _environmentUrl = environmentUrl.TrimEnd('/');
            _ownsHttpClient = httpClient == null;
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<WhoAmIResult> WhoAmIAsync(CancellationToken cancellationToken = default)
        {
            var token = await _tokenSource
                .GetTokenAsync(_environmentUrl, cancellationToken)
                .ConfigureAwait(false);

            using (var request = new HttpRequestMessage(HttpMethod.Get, _environmentUrl + ApiVersionPath + "/WhoAmI"))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Add("OData-MaxVersion", "4.0");
                request.Headers.Add("OData-Version", "4.0");

                using (var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                        throw new DataverseWebApiException(response.StatusCode, body);

                    var serializer = new DataContractJsonSerializer(typeof(WhoAmIResult));
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(body)))
                        return (WhoAmIResult)serializer.ReadObject(stream);
                }
            }
        }

        public void Dispose()
        {
            if (_ownsHttpClient) _httpClient.Dispose();
        }
    }

    /// <summary>Named to avoid colliding with the SDK's <c>Microsoft.Crm.Sdk.Messages.WhoAmIResponse</c>.</summary>
    [DataContract]
    public sealed class WhoAmIResult
    {
        [DataMember(Name = "UserId")]
        public string UserId { get; set; }

        [DataMember(Name = "BusinessUnitId")]
        public string BusinessUnitId { get; set; }

        [DataMember(Name = "OrganizationId")]
        public string OrganizationId { get; set; }
    }

    public sealed class DataverseWebApiException : Exception
    {
        public DataverseWebApiException(HttpStatusCode statusCode, string responseBody)
            : base($"Dataverse Web API returned {(int)statusCode} {statusCode}. {responseBody}")
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
        }

        public HttpStatusCode StatusCode { get; }

        public string ResponseBody { get; }
    }
}

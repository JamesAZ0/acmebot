using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Acmebot.App.Options;

namespace Acmebot.App.Providers;

public class EasyDnsProvider(EasyDnsOptions options) : IDnsProvider
{
    private readonly EasyDnsClient _easyDnsClient = new(options.ApiToken, options.ApiKey);

    public string Name => "EasyDNS";

    public TimeSpan PropagationDelay => TimeSpan.FromSeconds(60);

    public async Task<IReadOnlyList<DnsZone>> ListZonesAsync(CancellationToken cancellationToken = default)
    {
        var zones = await _easyDnsClient.ListDomainsAsync(cancellationToken);

        return zones.Select(x => new DnsZone(this) { Id = x.Name, Name = x.Name }).ToArray();
    }

    public async Task CreateTxtRecordAsync(DnsZone zone, string relativeRecordName, string[] values, CancellationToken cancellationToken = default)
    {
        var zoneName = zone.Name;

        foreach (var value in values)
        {
            var record = new RecordParam
            {
                Domain = zoneName,
                Host = relativeRecordName,
                Type = "TXT",
                Ttl = 60,
                RData = value
            };

            await _easyDnsClient.CreateRecordAsync(zoneName, record, cancellationToken);
        }
    }

    public async Task DeleteTxtRecordAsync(DnsZone zone, string relativeRecordName, CancellationToken cancellationToken = default)
    {
        var zoneName = zone.Name;

        var records = await _easyDnsClient.ListRecordsAsync(zoneName, cancellationToken);

        var recordsToDelete = records.Where(x => x.Host == relativeRecordName && x.Type == "TXT");

        foreach (var record in recordsToDelete)
        {
            await _easyDnsClient.DeleteRecordAsync(zoneName, record.Id, cancellationToken);
        }
    }

    private class EasyDnsClient
    {
        public EasyDnsClient(string apiToken, string apiKey)
        {
            _httpClient = new HttpClient(new BasicAuthHandler(apiToken, apiKey))
            {
                BaseAddress = new Uri("https://rest.easydns.net/")
            };

            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private readonly HttpClient _httpClient;

        public async Task<IReadOnlyList<DomainIndexEntry>> ListDomainsAsync(CancellationToken cancellationToken = default)
        {
            var userResponse = await _httpClient.GetFromJsonAsync<UserInfoResponse>("user", cancellationToken);
            var user = userResponse?.Data.User;

            var domainResponse = await _httpClient.GetFromJsonAsync<DomainListResponse>($"domains/list/{user}", cancellationToken);

            if (domainResponse?.Data is null)
            {
                return [];
            }

            return domainResponse.Data
                .Where(x => x.Key != "user")
                .Select(x => new DomainIndexEntry
                {
                    Name = x.Value.GetProperty("name").GetString()!
                })
                .ToArray();
        }

        public async Task<IReadOnlyList<Record>> ListRecordsAsync(string zone, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.GetFromJsonAsync<ZoneListResponse>($"zones/records/all/{zone}", cancellationToken);

            return response?.Data ?? [];
        }

        public async Task DeleteRecordAsync(string zone, long recordId, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.DeleteAsync($"zones/async/ux/records/{zone}/{recordId}", cancellationToken);

            response.EnsureSuccessStatusCode();
        }

        public async Task CreateRecordAsync(string zone, RecordParam txtRecord, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.PutAsJsonAsync($"zones/async/ux/records/add/{zone}/{txtRecord.Type}", txtRecord, cancellationToken);

            response.EnsureSuccessStatusCode();
        }

        private sealed class BasicAuthHandler(string apiToken, string apiKey) : DelegatingHandler(new HttpClientHandler())
        {
            private readonly string _basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiToken}:{apiKey}"));

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _basicAuth);

                return base.SendAsync(request, cancellationToken);
            }
        }
    }

    internal class UserInfoResponse
    {
        [JsonPropertyName("data")]
        public required UserInfoData Data { get; set; }
    }

    internal class UserInfoData
    {
        [JsonPropertyName("user")]
        public required string User { get; set; }
    }

    internal class DomainListResponse
    {
        [JsonPropertyName("data")]
        public required Dictionary<string, JsonElement> Data { get; set; }
    }

    internal class DomainIndexEntry
    {
        public required string Name { get; set; }
    }

    internal class ZoneListResponse
    {
        [JsonPropertyName("data")]
        public Record[]? Data { get; set; }
    }

    internal class RecordParam
    {
        [JsonPropertyName("domain")]
        public required string Domain { get; set; }

        [JsonPropertyName("host")]
        public required string Host { get; set; }

        [JsonPropertyName("type")]
        public required string Type { get; set; }

        [JsonPropertyName("rdata")]
        public required string RData { get; set; }

        [JsonPropertyName("ttl")]
        public int Ttl { get; set; }

        [JsonPropertyName("prio")]
        public int Prio { get; set; }
    }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    internal class Record : RecordParam
    {
        [JsonPropertyName("id")]
        public required long Id { get; set; }
    }
}

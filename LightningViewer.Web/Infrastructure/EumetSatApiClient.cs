using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LightningViewer.Web.Infrastructure;

/// <summary>
/// Client for the EUMETSAT Data Store REST API.
/// Handles OAuth2 Client Credentials flow, product search, and file download.
///
/// ⚠️ CREDENTIALS: You need a EUMETSAT API account (separate from Google).
///    Register at https://api.eumetsat.int and generate Consumer Key / Consumer Secret.
///    Set the values in appsettings.json under EumetSat:ConsumerKey and EumetSat:ConsumerSecret.
/// </summary>
public class EumetSatApiClient
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly IConfiguration     _config;
    private readonly ILogger<EumetSatApiClient> _logger;

    private string?  _accessToken;
    private DateTime _tokenExpiry = DateTime.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public EumetSatApiClient(
        IHttpClientFactory clientFactory,
        IConfiguration     config,
        ILogger<EumetSatApiClient> logger)
    {
        _clientFactory = clientFactory;
        _config        = config;
        _logger        = logger;
    }

    // ─── Token Management ───────────────────────────────────────────────────

    private async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_accessToken != null && DateTime.UtcNow < _tokenExpiry.AddMinutes(-1))
                return _accessToken;

            var key    = _config["EumetSat:ConsumerKey"]
                ?? throw new InvalidOperationException("EumetSat:ConsumerKey not configured.");
            var secret = _config["EumetSat:ConsumerSecret"]
                ?? throw new InvalidOperationException("EumetSat:ConsumerSecret not configured.");
            var endpoint = _config["EumetSat:TokenEndpoint"] ?? "https://api.eumetsat.int/token";

            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{key}:{secret}"));

            using var client  = _clientFactory.CreateClient("EumetSat");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            _accessToken = doc.RootElement.GetProperty("access_token").GetString()!;
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var expEl)
                ? expEl.GetInt32() : 3600;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn);

            _logger.LogInformation("EUMETSAT OAuth token acquired, expires in {s}s", expiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    // ─── Product Search ──────────────────────────────────────────────────────

    /// <summary>
    /// Searches for available AF products within the given time range.
    /// Collection ID: EO:EUM:DAT:MTG:LI-L2-AF
    /// </summary>
    public async Task<List<EumetSatProduct>> SearchProductsAsync(
        DateTime startTime, DateTime endTime, CancellationToken ct = default)
    {
        var collectionId = _config["EumetSat:CollectionId"] ?? "EO:EUM:DAT:0691";
        var token = await GetAccessTokenAsync(ct);

        // EUMETSAT OpenSearch endpoint
        var url = "https://api.eumetsat.int/data/search-products/os"
                + $"?pi={Uri.EscapeDataString(collectionId)}"
                + $"&dtstart={startTime:yyyy-MM-ddTHH:mm:ssZ}"
                + $"&dtend={endTime:yyyy-MM-ddTHH:mm:ssZ}"
                + "&si=0&c=500&or=asc&format=json";

        using var client  = _clientFactory.CreateClient("EumetSat");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseProductsResponse(json);
    }

    // ─── Product Download ─────────────────────────────────────────────────────

    /// <summary>
    /// Downloads a product file as a byte array (NetCDF-4 / HDF5 format).
    /// </summary>
    public async Task<byte[]> DownloadProductAsync(string downloadUrl, CancellationToken ct = default)
    {
        var token = await GetAccessTokenAsync(ct);

        using var client  = _clientFactory.CreateClient("EumetSat");
        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    // ─── JSON Parsing ─────────────────────────────────────────────────────────

    private List<EumetSatProduct> ParseProductsResponse(string json)
    {
        var products = new List<EumetSatProduct>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Try GeoJSON FeatureCollection format (most common for EUMETSAT)
            if (root.TryGetProperty("features", out var features))
            {
                foreach (var feature in features.EnumerateArray())
                {
                    var p = ParseFeature(feature);
                    if (p != null) products.Add(p);
                }
            }
            // Try flat products array
            else if (root.TryGetProperty("products", out var prods))
            {
                foreach (var prod in prods.EnumerateArray())
                {
                    var p = ParseProductEntry(prod);
                    if (p != null) products.Add(p);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse EUMETSAT product search response");
        }
        return products;
    }

    private static EumetSatProduct? ParseFeature(JsonElement feature)
    {
        try
        {
            var props = feature.GetProperty("properties");
            var id    = props.TryGetProperty("identifier", out var idEl) ? idEl.GetString()
                      : props.TryGetProperty("title",      out var tEl)  ? tEl.GetString()
                      : null;

            // Parse sensing time (EUMETSAT uses date: "start/end")
            DateTime sensingTime = DateTime.UtcNow;
            if (props.TryGetProperty("date", out var dateEl) && dateEl.GetString() is string dStr)
            {
                var parts = dStr.Split('/');
                DateTime.TryParse(parts[0], null, System.Globalization.DateTimeStyles.RoundtripKind, out sensingTime);
            }
            else if (props.TryGetProperty("start_datetime", out var sdEl) && sdEl.GetString() is string sdStr)
            {
                DateTime.TryParse(sdStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out sensingTime);
            }

            // Find download URL (EUMETSAT OS uses properties.links.data[0].href)
            string? downloadUrl = null;
            if (props.TryGetProperty("links", out var links) && links.TryGetProperty("data", out var dataArr))
            {
                foreach (var link in dataArr.EnumerateArray())
                {
                    downloadUrl = link.TryGetProperty("href", out var href) ? href.GetString() : null;
                    if (!string.IsNullOrEmpty(downloadUrl)) break;
                }
            }

            // Fallback for STAC style
            if (downloadUrl == null && feature.TryGetProperty("assets", out var assets))
            {
                if (assets.TryGetProperty("product", out var asset))
                    downloadUrl = asset.TryGetProperty("href", out var href) ? href.GetString() : null;
            }

            return (id == null || downloadUrl == null)
                ? null
                : new EumetSatProduct(id, downloadUrl, sensingTime);
        }
        catch { return null; }
    }

    private static EumetSatProduct? ParseProductEntry(JsonElement prod)
    {
        try
        {
            var id  = prod.TryGetProperty("identifier", out var idEl) ? idEl.GetString() : null;
            var url = prod.TryGetProperty("url",  out var uEl) ? uEl.GetString()
                    : prod.TryGetProperty("href", out var hEl) ? hEl.GetString() : null;
            return (id == null || url == null) ? null : new EumetSatProduct(id, url, DateTime.UtcNow);
        }
        catch { return null; }
    }
}

/// <summary>Represents a single EUMETSAT product entry returned by the search API.</summary>
public record EumetSatProduct(string Id, string DownloadUrl, DateTime SensingTime);

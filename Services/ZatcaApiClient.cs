using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ZatcaFatooraDotNet8Sample.Models.Configuration;
using ZatcaFatooraDotNet8Sample.Models.Responses;

namespace ZatcaFatooraDotNet8Sample.Services;

public sealed class ZatcaApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly ZatcaSettings _settings;

    public ZatcaApiClient(HttpClient httpClient, IOptions<ZatcaSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _httpClient.BaseAddress = new Uri(_settings.BaseUrl.TrimEnd('/') + "/");
    }

    public async Task<(ZatcaCertificateResponse? Response, ApiProxyResponse Raw)> CreateComplianceCsidAsync(string csrBase64, string otp, CancellationToken cancellationToken)
    {
        var body = new { csr = csrBase64 };
        using var request = CreateRequest(HttpMethod.Post, _settings.ComplianceCsidEndpoint, body);
        request.Headers.Add("OTP", otp);

        var raw = await SendAsync(request, cancellationToken);
        var parsed = TryDeserialize<ZatcaCertificateResponse>(raw.Content);
        return (parsed, raw);
    }

    public async Task<(ZatcaCertificateResponse? Response, ApiProxyResponse Raw)> CreateProductionCsidAsync(string complianceRequestId, string binarySecurityToken, string secret, CancellationToken cancellationToken)
    {
        var body = new { compliance_request_id = complianceRequestId };
        using var request = CreateRequest(HttpMethod.Post, _settings.ProductionCsidEndpoint, body);
        AddBasicAuth(request, binarySecurityToken, secret);

        var raw = await SendAsync(request, cancellationToken);
        var parsed = TryDeserialize<ZatcaCertificateResponse>(raw.Content);
        return (parsed, raw);
    }

    public Task<ApiProxyResponse> CheckComplianceInvoiceAsync(string binarySecurityToken, string secret, string invoiceHash, string uuid, string invoiceBase64, CancellationToken cancellationToken)
        => SubmitInvoiceAsync(_settings.ComplianceChecksEndpoint, binarySecurityToken, secret, invoiceHash, uuid, invoiceBase64, cancellationToken);

    public Task<ApiProxyResponse> ReportInvoiceAsync(string binarySecurityToken, string secret, string invoiceHash, string uuid, string invoiceBase64, CancellationToken cancellationToken)
        => SubmitInvoiceAsync(_settings.ReportingEndpoint, binarySecurityToken, secret, invoiceHash, uuid, invoiceBase64, cancellationToken, clearanceStatus: "0");

    public Task<ApiProxyResponse> ClearInvoiceAsync(string binarySecurityToken, string secret, string invoiceHash, string uuid, string invoiceBase64, CancellationToken cancellationToken)
        => SubmitInvoiceAsync(_settings.ClearanceEndpoint, binarySecurityToken, secret, invoiceHash, uuid, invoiceBase64, cancellationToken, clearanceStatus: "1");

    private async Task<ApiProxyResponse> SubmitInvoiceAsync(
        string relativeUrl,
        string binarySecurityToken,
        string secret,
        string invoiceHash,
        string uuid,
        string invoiceBase64,
        CancellationToken cancellationToken,
        string? clearanceStatus = null)
    {
        var body = new
        {
            invoiceHash,
            uuid,
            invoice = invoiceBase64
        };

        using var request = CreateRequest(HttpMethod.Post, relativeUrl, body);
        AddBasicAuth(request, binarySecurityToken, secret);

        if (!string.IsNullOrWhiteSpace(clearanceStatus))
        {
            request.Headers.Add("Clearance-Status", clearanceStatus);
        }

        return await SendAsync(request, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativeUrl, object body)
    {
        var request = new HttpRequestMessage(method, relativeUrl.TrimStart('/'));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("Accept-Version", _settings.AcceptVersion);
        request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        return request;
    }

    private static void AddBasicAuth(HttpRequestMessage request, string binarySecurityToken, string secret)
    {
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{binarySecurityToken}:{secret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private async Task<ApiProxyResponse> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        return new ApiProxyResponse
        {
            StatusCode = (int)response.StatusCode,
            Content = content
        };
    }

    private static T? TryDeserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch
        {
            return default;
        }
    }
}

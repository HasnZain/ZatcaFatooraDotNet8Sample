using System.Text.Json.Serialization;

namespace ZatcaFatooraDotNet8Sample.Models.Responses;

public sealed class ZatcaCertificateResponse
{
    [JsonPropertyName("requestID")]
    public long RequestId { get; set; }

    [JsonPropertyName("dispositionMessage")]
    public string? DispositionMessage { get; set; }

    [JsonPropertyName("binarySecurityToken")]
    public string? BinarySecurityToken { get; set; }

    [JsonPropertyName("secret")]
    public string? Secret { get; set; }

    [JsonPropertyName("errors")]
    public object? Errors { get; set; }

    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; set; }
}

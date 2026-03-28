using System.Text.Json.Serialization;

namespace ZatcaFatooraDotNet8Sample.Models.Responses;

public sealed class ZatcaCertificateResponse
{
    [JsonPropertyName("requestID")]
    public string? RequestId { get; set; }

    [JsonPropertyName("binarySecurityToken")]
    public string? BinarySecurityToken { get; set; }

    [JsonPropertyName("secret")]
    public string? Secret { get; set; }

    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? AdditionalData { get; set; }
}

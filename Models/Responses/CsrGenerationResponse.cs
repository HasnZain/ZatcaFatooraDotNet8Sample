namespace ZatcaFatooraDotNet8Sample.Models.Responses;

public sealed class CsrGenerationResponse
{
    public string SubjectName { get; set; } = string.Empty;
    public string PrivateKeyPem { get; set; } = string.Empty;
    public string PublicKeyPem { get; set; } = string.Empty;
    public string CsrPem { get; set; } = string.Empty;
    public string CsrBase64 { get; set; } = string.Empty;
}

namespace ZatcaFatooraDotNet8Sample.Models.Domain;

public sealed class DeviceState
{
    public string? SubjectName { get; set; }
    public string? PrivateKeyPem { get; set; }
    public string? PublicKeyPem { get; set; }
    public string? CsrPem { get; set; }
    public string? CsrBase64 { get; set; }

    public string? ComplianceRequestId { get; set; }
    public string? ComplianceBinarySecurityToken { get; set; }
    public string? ComplianceSecret { get; set; }

    public string? ProductionBinarySecurityToken { get; set; }
    public string? ProductionSecret { get; set; }

    public long LastInvoiceCounterValue { get; set; }
    public string? LastInvoiceHashBase64 { get; set; }
}

namespace ZatcaFatooraDotNet8Sample.Models.Configuration;

public sealed class ZatcaSettings
{
    public const string SectionName = "Zatca";

    public string BaseUrl { get; set; } = string.Empty;
    public string ComplianceCsidEndpoint { get; set; } = "/compliance";
    public string ComplianceChecksEndpoint { get; set; } = "/compliance/invoices";
    public string ProductionCsidEndpoint { get; set; } = "/production/csids";
    public string ReportingEndpoint { get; set; } = "/invoices/reporting/single";
    public string ClearanceEndpoint { get; set; } = "/invoices/clearance/single";
    public string AcceptVersion { get; set; } = "V2";
    public string StateFilePath { get; set; } = "App_Data/device-state.json";
    public string DefaultCurrencyCode { get; set; } = "SAR";
    public string DefaultTaxCurrencyCode { get; set; } = "SAR";
}

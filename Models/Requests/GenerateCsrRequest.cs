using System.ComponentModel.DataAnnotations;

public sealed class GenerateCsrRequest
{
    [Required]
    public string CommonName { get; set; } = string.Empty;

    [Required]
    public string OrganizationName { get; set; } = string.Empty;

    [Required]
    public string OrganizationalUnitName { get; set; } = "Main Branch";

    [Required]
    [StringLength(2, MinimumLength = 2)]
    public string CountryCode { get; set; } = "SA";

    // Keep for your app if needed, but do not include in CSR subject for now
    public string? EmailAddress { get; set; }

    [Required]
    public string SerialNumber { get; set; } = string.Empty;

    [Required]
    public string OrganizationIdentifier { get; set; } = string.Empty;

    [Required]
    public string InvoiceType { get; set; } = string.Empty;

    [Required]
    public string LocationAddress { get; set; } = string.Empty;

    [Required]
    public string IndustryBusinessCategory { get; set; } = string.Empty;
}
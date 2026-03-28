namespace ZatcaFatooraDotNet8Sample.Models.Domain;

public sealed class PartyInfo
{
    public string RegistrationName { get; set; } = string.Empty;
    public string VatNumber { get; set; } = string.Empty;
    public string? RegistrationNumber { get; set; }
    public string? StreetName { get; set; }
    public string? BuildingNumber { get; set; }
    public string? PlotIdentification { get; set; }
    public string? CitySubdivisionName { get; set; }
    public string? CityName { get; set; }
    public string? PostalZone { get; set; }
    public string CountryCode { get; set; } = "SA";
}

namespace ZatcaFatooraDotNet8Sample.Models.Domain;

public sealed class InvoiceLineItem
{
    public string Name { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxRate { get; set; } = 15m;
    public string TaxCategoryCode { get; set; } = "S";
    public string UnitCode { get; set; } = "PCE";
}

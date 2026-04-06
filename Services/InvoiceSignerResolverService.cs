namespace ZatcaFatooraDotNet8Sample.Services;

public sealed class InvoiceSignerResolverService
{
    private readonly InvoiceSignerService _standardSigner;
    private readonly SimplifiedInvoiceSignerService _simplifiedSigner;

    public InvoiceSignerResolverService(
        InvoiceSignerService standardSigner,
        SimplifiedInvoiceSignerService simplifiedSigner)
    {
        _standardSigner = standardSigner;
        _simplifiedSigner = simplifiedSigner;
    }

    public IInvoiceSignerService Resolve(string invoiceCategory)
        => string.Equals(invoiceCategory, "Simplified", StringComparison.OrdinalIgnoreCase)
            ? (IInvoiceSignerService)_simplifiedSigner
            : _standardSigner;
}
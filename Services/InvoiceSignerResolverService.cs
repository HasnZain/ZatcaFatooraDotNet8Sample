namespace ZatcaFatooraDotNet8Sample.Services;

public sealed class InvoiceSignerResolverService
{
    private readonly SimplifiedInvoiceSignerService _simplifiedSigner;

    public InvoiceSignerResolverService(SimplifiedInvoiceSignerService simplifiedSigner)
        => _simplifiedSigner = simplifiedSigner;

    public IInvoiceSignerService Resolve(string invoiceCategory)
    {
        _ = invoiceCategory;
        return _simplifiedSigner;
    }
}
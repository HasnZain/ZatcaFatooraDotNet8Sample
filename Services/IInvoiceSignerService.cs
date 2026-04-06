using ZatcaFatooraDotNet8Sample.Models.Domain;

namespace ZatcaFatooraDotNet8Sample.Services
{
    public interface IInvoiceSignerService
    {
        SignedInvoiceArtifacts Sign(string unsignedXml, string privateKeyPem, string? binarySecurityToken);
        string ComputeInvoiceHash(string xml);
    }
}

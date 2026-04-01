using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using ZatcaFatooraDotNet8Sample.Models.Domain;
using ZatcaFatooraDotNet8Sample.Utilities;

namespace ZatcaFatooraDotNet8Sample.Services;

public sealed class InvoiceSignerService
{
    // ECDSA-SHA256 signature method URI as defined in RFC 4051 / W3C XML-DSig-more
    private const string XmlDsigEcdsaSha256Url = "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256";

    public SignedInvoiceArtifacts Sign(string unsignedXml, string privateKeyPem, string? binarySecurityToken)
    {
        var invoiceHashBase64 = ComputeInvoiceHash(unsignedXml);

        // ZATCA requires ECDSA (secp256k1) — the private key exported by CsrGeneratorService is EC,
        // so we must import it as ECDsa, not RSA.
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);

        var hashBytes = Convert.FromBase64String(invoiceHashBase64);
        // ECDsa.SignHash uses the curve's native algorithm; no padding parameter needed.
        var signatureBase64 = Convert.ToBase64String(ecdsa.SignHash(hashBytes));

        var signedXml = ApplyXmlSignature(unsignedXml, ecdsa, binarySecurityToken);

        string? publicKeyBase64 = null;
        string? certificateSignatureBase64 = null;

        if (!string.IsNullOrWhiteSpace(binarySecurityToken))
        {
            var certificate = new X509Certificate2(Convert.FromBase64String(binarySecurityToken));
            publicKeyBase64 = Convert.ToBase64String(certificate.GetPublicKey());
            // Tag 9 of the ZATCA QR is the certificate's own digital signature bytes.
            certificateSignatureBase64 = Convert.ToBase64String(certificate.GetCertHash(HashAlgorithmName.SHA256));
        }

        return new SignedInvoiceArtifacts
        {
            SignedXml = signedXml,
            InvoiceHashBase64 = invoiceHashBase64,
            SignatureBase64 = signatureBase64,
            PublicKeyBase64 = publicKeyBase64,
            CertificateSignatureBase64 = certificateSignatureBase64
        };
    }

    public string ComputeInvoiceHash(string xml)
    {
        var xmlDoc = XmlUtility.LoadXml(xml);
        var namespaceManager = CreateNamespaceManager(xmlDoc.NameTable);

        RemoveNodes(xmlDoc, "//ext:UBLExtensions", namespaceManager);
        RemoveNodes(xmlDoc, "//cac:AdditionalDocumentReference[cbc:ID='QR']", namespaceManager);
        RemoveNodes(xmlDoc, "//*[local-name()='Signature']", namespaceManager);

        var canonicalBytes = XmlUtility.Canonicalize(xmlDoc);
        var hashBytes = HashUtility.Sha256Bytes(canonicalBytes);
        return Convert.ToBase64String(hashBytes);
    }

    private static string ApplyXmlSignature(string xml, ECDsa ecdsa, string? binarySecurityToken)
    {
        var document = new XmlDocument
        {
            PreserveWhitespace = false
        };
        document.LoadXml(xml);

        var signedXml = new SignedXml(document)
        {
            SigningKey = ecdsa,
            SignedInfo =
            {
                CanonicalizationMethod = SignedXml.XmlDsigCanonicalizationUrl,
                SignatureMethod = XmlDsigEcdsaSha256Url
            }
        };

        var reference = new Reference
        {
            Uri = string.Empty,
            DigestMethod = SignedXml.XmlDsigSHA256Url
        };

        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigC14NTransform());
        signedXml.AddReference(reference);

        if (!string.IsNullOrWhiteSpace(binarySecurityToken))
        {
            var certificate = new X509Certificate2(Convert.FromBase64String(binarySecurityToken));
            var keyInfo = new KeyInfo();
            keyInfo.AddClause(new KeyInfoX509Data(certificate));
            signedXml.KeyInfo = keyInfo;
        }

        signedXml.ComputeSignature();
        var signatureElement = signedXml.GetXml();

        var ns = CreateNamespaceManager(document.NameTable);
        var extensionContent = document.SelectSingleNode("/*/ext:UBLExtensions/ext:UBLExtension/ext:ExtensionContent", ns);

        if (extensionContent is null)
        {
            document.DocumentElement?.AppendChild(document.ImportNode(signatureElement, true));
        }
        else
        {
            extensionContent.AppendChild(document.ImportNode(signatureElement, true));
        }

        return document.OuterXml;
    }

    private static void RemoveNodes(XmlDocument document, string xpath, XmlNamespaceManager namespaceManager)
    {
        var nodes = document.SelectNodes(xpath, namespaceManager);
        if (nodes is null)
        {
            return;
        }

        foreach (XmlNode node in nodes)
        {
            node.ParentNode?.RemoveChild(node);
        }
    }

    private static XmlNamespaceManager CreateNamespaceManager(XmlNameTable nameTable)
    {
        var namespaceManager = new XmlNamespaceManager(nameTable);
        namespaceManager.AddNamespace("cbc", "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2");
        namespaceManager.AddNamespace("cac", "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2");
        namespaceManager.AddNamespace("ext", "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2");
        namespaceManager.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);
        return namespaceManager;
    }
}

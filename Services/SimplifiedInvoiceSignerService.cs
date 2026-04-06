using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using ZatcaFatooraDotNet8Sample.Models.Domain;
using ZatcaFatooraDotNet8Sample.Utilities;

namespace ZatcaFatooraDotNet8Sample.Services;

public sealed class SimplifiedInvoiceSignerService : IInvoiceSignerService
{
    private const string XmlDsigEcdsaSha256Url = "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256";
    private const string XadesNamespace = "http://uri.etsi.org/01903/v1.3.2#";
    private const string UblSignatureId = "urn:oasis:names:specification:ubl:signature:Invoice";
    private const string UblSignatureInformationId = "urn:oasis:names:specification:ubl:signature:1";

    public SignedInvoiceArtifacts Sign(string unsignedXml, string privateKeyPem, string? binarySecurityToken)
    {
        var invoiceHashBase64 = ComputeInvoiceHash(unsignedXml);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);

        X509Certificate2? certificate = null;
        string? publicKeyBase64 = null;
        string? certificateSignatureBase64 = null;

        if (!string.IsNullOrWhiteSpace(binarySecurityToken))
        {
            // FIX: CertificateHelper handles ZATCA's double-base64 encoding.
            // token = Base64(Base64(DER)) — two decodes needed to reach raw DER bytes.
            certificate = CertificateHelper.LoadFromBinarySecurityToken(binarySecurityToken);

            // QR Tag 8: uncompressed EC public key (04 || X || Y) from the certificate.
            using var certEcdsa = certificate.GetECDsaPublicKey();
            if (certEcdsa is not null)
            {
                var ecParams = certEcdsa.ExportParameters(false);
                if (ecParams.Q.X is not null && ecParams.Q.Y is not null)
                {
                    var uncompressed = new byte[1 + ecParams.Q.X.Length + ecParams.Q.Y.Length];
                    uncompressed[0] = 0x04;
                    Buffer.BlockCopy(ecParams.Q.X, 0, uncompressed, 1, ecParams.Q.X.Length);
                    Buffer.BlockCopy(ecParams.Q.Y, 0, uncompressed, 1 + ecParams.Q.X.Length, ecParams.Q.Y.Length);
                    publicKeyBase64 = Convert.ToBase64String(uncompressed);
                }
            }

            // QR Tag 9: SHA-256 of the certificate's raw DER bytes.
            certificateSignatureBase64 = Convert.ToBase64String(
                HashUtility.Sha256Bytes(certificate.RawData));
        }

        var signedXml = ApplyUblWrappedSignature(unsignedXml, ecdsa, certificate);

        // QR Tag 7: the actual ds:SignatureValue embedded in the signed XML,
        // NOT a raw ecdsa.SignHash() — they produce different byte encodings.
        var signatureBase64 = ExtractSignatureValue(signedXml);

        return new SignedInvoiceArtifacts
        {
            SignedXml = signedXml,
            InvoiceHashBase64 = invoiceHashBase64,
            SignatureBase64 = signatureBase64,
            PublicKeyBase64 = publicKeyBase64,
            CertificateSignatureBase64 = certificateSignatureBase64
        };
    }

    private static string ExtractSignatureValue(string signedXml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(signedXml);
        var node = doc.SelectSingleNode("//*[local-name()='SignatureValue']");
        if (node is null || string.IsNullOrWhiteSpace(node.InnerText))
            throw new InvalidOperationException("ds:SignatureValue was not found in signed XML.");
        return node.InnerText.Trim();
    }

    public string ComputeInvoiceHash(string xml)
    {
        var xmlDoc = XmlUtility.LoadXml(xml);
        var ns = CreateNamespaceManager(xmlDoc.NameTable);
        RemoveNodes(xmlDoc, "//ext:UBLExtensions", ns);
        RemoveNodes(xmlDoc, "//cac:AdditionalDocumentReference[cbc:ID='QR']", ns);
        RemoveNodes(xmlDoc, "//*[local-name()='Signature']", ns);
        var canonicalBytes = XmlUtility.Canonicalize(xmlDoc);
        return Convert.ToBase64String(HashUtility.Sha256Bytes(canonicalBytes));
    }

    private static string ApplyUblWrappedSignature(string xml, ECDsa ecdsa, X509Certificate2? certificate)
    {
        const string signatureElementId = "signature";
        const string signedPropertiesId = "xadesSignedProperties";

        var document = new XmlDocument { PreserveWhitespace = false };
        document.LoadXml(xml);

        var signedXml = new SignedXml(document)
        {
            SigningKey = ecdsa
        };
        signedXml.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigCanonicalizationUrl;
        signedXml.SignedInfo.SignatureMethod = XmlDsigEcdsaSha256Url;
        signedXml.Signature.Id = signatureElementId;

        // Reference 1: the invoice document itself
        var documentReference = new Reference
        {
            Uri = string.Empty,
            DigestMethod = SignedXml.XmlDsigSHA256Url
        };
        documentReference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        documentReference.AddTransform(new XmlDsigC14NTransform());
        signedXml.AddReference(documentReference);

        if (certificate is not null)
        {
            var keyInfo = new KeyInfo { Id = "certificate" };
            keyInfo.AddClause(new KeyInfoX509Data(certificate));
            signedXml.KeyInfo = keyInfo;
        }

        // Build QualifyingProperties using the SAME document instance.
        // This is required so SignedXml can resolve "#xadesSignedProperties" by Id
        // when computing the digest for Reference 2 below.
        // (Using a separate XmlDocument causes "Malformed reference element" exception.)
        var qualifyingPropertiesElement = BuildQualifyingProperties(
            document, certificate, "#" + signatureElementId, signedPropertiesId);

        // Temporarily attach to document root so the Id lookup succeeds during ComputeSignature.
        document.DocumentElement!.AppendChild(qualifyingPropertiesElement);

        // Reference 2: xades:SignedProperties
        // Committing this reference is what makes signed-properties-hashing valid.
        var signedPropertiesReference = new Reference
        {
            Uri = "#" + signedPropertiesId,
            Type = "http://uri.etsi.org/01903#SignedProperties",
            DigestMethod = SignedXml.XmlDsigSHA256Url
        };
        signedPropertiesReference.AddTransform(new XmlDsigC14NTransform());
        signedXml.AddReference(signedPropertiesReference);

        signedXml.ComputeSignature();
        var dsSignature = signedXml.GetXml();

        // Remove the temporary node from the document root.
        document.DocumentElement.RemoveChild(qualifyingPropertiesElement);

        // Place the UBL wrapper inside ext:ExtensionContent.
        var ns = CreateNamespaceManager(document.NameTable);
        var extensionContent = document.SelectSingleNode(
            "/*/ext:UBLExtensions/ext:UBLExtension/ext:ExtensionContent", ns);

        if (extensionContent is null)
            throw new InvalidOperationException("UBL ExtensionContent node was not found.");

        extensionContent.RemoveAll();

        var ublDocumentSignatures = document.CreateElement(
            "sig", "UBLDocumentSignatures",
            "urn:oasis:names:specification:ubl:schema:xsd:CommonSignatureComponents-2");

        var signatureInformation = document.CreateElement(
            "sac", "SignatureInformation",
            "urn:oasis:names:specification:ubl:schema:xsd:SignatureAggregateComponents-2");

        // BR-KSA-28: must be exactly "urn:oasis:names:specification:ubl:signature:1"
        var signatureInfoId = document.CreateElement(
            "cbc", "ID",
            "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2");
        signatureInfoId.InnerText = UblSignatureInformationId;

        var referencedSignatureId = document.CreateElement(
            "sbc", "ReferencedSignatureID",
            "urn:oasis:names:specification:ubl:schema:xsd:SignatureBasicComponents-2");
        referencedSignatureId.InnerText = UblSignatureId;

        signatureInformation.AppendChild(signatureInfoId);
        signatureInformation.AppendChild(referencedSignatureId);
        signatureInformation.AppendChild(document.ImportNode(dsSignature, true));
        ublDocumentSignatures.AppendChild(signatureInformation);
        extensionContent.AppendChild(ublDocumentSignatures);

        return document.OuterXml;
    }

    private static XmlElement BuildQualifyingProperties(
        XmlDocument document,
        X509Certificate2? certificate,
        string target,
        string signedPropertiesId)
    {
        var qualifyingProperties = document.CreateElement("xades", "QualifyingProperties", XadesNamespace);
        qualifyingProperties.SetAttribute("Target", target);

        var signedProperties = document.CreateElement("xades", "SignedProperties", XadesNamespace);
        signedProperties.SetAttribute("Id", signedPropertiesId);

        var signedSignatureProperties = document.CreateElement(
            "xades", "SignedSignatureProperties", XadesNamespace);

        var signingTime = document.CreateElement("xades", "SigningTime", XadesNamespace);
        signingTime.InnerText = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        signedSignatureProperties.AppendChild(signingTime);

        if (certificate is not null)
        {
            var signingCertificate = document.CreateElement("xades", "SigningCertificate", XadesNamespace);
            var cert = document.CreateElement("xades", "Cert", XadesNamespace);
            var certDigest = document.CreateElement("xades", "CertDigest", XadesNamespace);

            var digestMethod = document.CreateElement("ds", "DigestMethod", SignedXml.XmlDsigNamespaceUrl);
            digestMethod.SetAttribute("Algorithm", SignedXml.XmlDsigSHA256Url);

            // SHA-256 of raw DER bytes — the only value ZATCA accepts for certificate-hashing.
            var digestValue = document.CreateElement("ds", "DigestValue", SignedXml.XmlDsigNamespaceUrl);
            digestValue.InnerText = Convert.ToBase64String(HashUtility.Sha256Bytes(certificate.RawData));

            certDigest.AppendChild(digestMethod);
            certDigest.AppendChild(digestValue);

            var issuerSerial = document.CreateElement("xades", "IssuerSerial", XadesNamespace);
            var issuerName = document.CreateElement("ds", "X509IssuerName", SignedXml.XmlDsigNamespaceUrl);
            issuerName.InnerText = certificate.IssuerName.Name ?? string.Empty;

            var serialNumber = document.CreateElement("ds", "X509SerialNumber", SignedXml.XmlDsigNamespaceUrl);
            serialNumber.InnerText = ToDecimalSerialNumber(certificate);

            issuerSerial.AppendChild(issuerName);
            issuerSerial.AppendChild(serialNumber);
            cert.AppendChild(certDigest);
            cert.AppendChild(issuerSerial);
            signingCertificate.AppendChild(cert);
            signedSignatureProperties.AppendChild(signingCertificate);
        }

        signedProperties.AppendChild(signedSignatureProperties);
        qualifyingProperties.AppendChild(signedProperties);
        return qualifyingProperties;
    }

    private static string ToDecimalSerialNumber(X509Certificate2 certificate)
    {
        var serial = new BigInteger(
            certificate.GetSerialNumber(), isUnsigned: true, isBigEndian: false);
        return serial.ToString(CultureInfo.InvariantCulture);
    }

    private static void RemoveNodes(XmlDocument document, string xpath, XmlNamespaceManager ns)
    {
        var nodes = document.SelectNodes(xpath, ns);
        if (nodes is null) return;
        foreach (XmlNode node in nodes)
            node.ParentNode?.RemoveChild(node);
    }

    private static XmlNamespaceManager CreateNamespaceManager(XmlNameTable nameTable)
    {
        var ns = new XmlNamespaceManager(nameTable);
        ns.AddNamespace("cbc", "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2");
        ns.AddNamespace("cac", "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2");
        ns.AddNamespace("ext", "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2");
        ns.AddNamespace("sig", "urn:oasis:names:specification:ubl:schema:xsd:CommonSignatureComponents-2");
        ns.AddNamespace("sac", "urn:oasis:names:specification:ubl:schema:xsd:SignatureAggregateComponents-2");
        ns.AddNamespace("sbc", "urn:oasis:names:specification:ubl:schema:xsd:SignatureBasicComponents-2");
        ns.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);
        ns.AddNamespace("xades", XadesNamespace);
        return ns;
    }
}
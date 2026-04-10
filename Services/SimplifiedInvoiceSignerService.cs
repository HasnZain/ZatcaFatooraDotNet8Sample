using System.Formats.Asn1;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Security;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text.RegularExpressions;
using System.Xml;
using ZatcaFatooraDotNet8Sample.Models.Domain;
using ZatcaFatooraDotNet8Sample.Utilities;

namespace ZatcaFatooraDotNet8Sample.Services;

public sealed class SimplifiedInvoiceSignerService : IInvoiceSignerService
{
    private const string XmlDsigEcdsaSha256Url = "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256";
    private const string XmlEncSha256Uri = "http://www.w3.org/2001/04/xmlenc#sha256";
    private const string XmlExcC14NUri = "http://www.w3.org/2001/10/xml-exc-c14n#";
    private const string ZatcaInvoicePolicyUri = "https://zatca.gov.sa/InvoicePolicy";
    private const string XadesNamespace = "http://uri.etsi.org/01903/v1.3.2#";
    private const string UblSignatureId = "urn:oasis:names:specification:ubl:signature:Invoice";
    private const string UblSignatureInformationId = "urn:oasis:names:specification:ubl:signature:1";

    public SignedInvoiceArtifacts Sign(string unsignedXml, string privateKeyPem, string? binarySecurityToken)
    {
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

            // QR Tag 8: SubjectPublicKeyInfo (PKCS#8) bytes — Salla ZATCA: base64_decode(plain PEM public key).
            using var certEcdsa = certificate.GetECDsaPublicKey();
            if (certEcdsa is not null)
                publicKeyBase64 = Convert.ToBase64String(certEcdsa.ExportSubjectPublicKeyInfo());

            // QR Tag 9: certificate signature BIT STRING bytes (full DER signature payload).
            var certSig = ExtractCertificateSignatureBytes(certificate.RawData);
            certificateSignatureBase64 = Convert.ToBase64String(certSig);
        }

        var signedXml = ApplyUblWrappedSignature(unsignedXml, ecdsa, certificate);

        // QR Tag 6: must match ds:DigestValue of the invoice SignedInfo Reference (not a separate hash path).
        var invoiceHashBase64 = ExtractInvoiceReferenceDigestValue(signedXml);

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

    private static string ExtractInvoiceReferenceDigestValue(string signedXml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(signedXml);
        var nsmgr = new XmlNamespaceManager(doc.NameTable);
        nsmgr.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);

        var reference = doc.SelectSingleNode(
            "//ds:SignedInfo/ds:Reference[@Id='invoiceSignedData']", nsmgr)
            ?? doc.SelectSingleNode(
                "//*[local-name()='SignedInfo']/*[local-name()='Reference' and (@URI='' or not(@URI))]",
                nsmgr);

        var digestValue = reference?.SelectSingleNode("*[local-name()='DigestValue']", nsmgr)?.InnerText.Trim();
        if (string.IsNullOrEmpty(digestValue))
            throw new InvalidOperationException("Invoice ds:Reference DigestValue was not found in signed XML.");
        return digestValue;
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
        // Match submitted signed-XML whitespace so TLV tag 6 matches ZATCA's invoice hash check.
        var xmlDoc = XmlUtility.LoadXml(xml, preserveWhitespace: true);
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

        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(xml);

        ZatcaXmlDsigC14N11Transform.EnsureCryptoRegistered();

        // ── (1) Prepare SignedProperties: build xades:QualifyingProperties / xades:SignedProperties (EPES).
        var qualifyingPropertiesElement = BuildQualifyingProperties(
            document, certificate, signatureElementId, signedPropertiesId);
        AttachQualifyingPropertiesInUblExtensionSignatureShell(document, qualifyingPropertiesElement);

        var signedXml = new SignedXml(document)
        {
            SigningKey = ecdsa
        };
        signedXml.SafeCanonicalizationMethods.Add(ZatcaXmlDsigC14N11Transform.AlgorithmUrl);
        signedXml.SafeCanonicalizationMethods.Add(XmlExcC14NUri);
        // SignedInfo is canonicalized with C14N 1.1; ECDSA-SHA256 signs those octets (XMLDSig).
        signedXml.SignedInfo.CanonicalizationMethod = ZatcaXmlDsigC14N11Transform.AlgorithmUrl;
        signedXml.SignedInfo.SignatureMethod = XmlDsigEcdsaSha256Url;
        signedXml.Signature.Id = signatureElementId;

        // Invoice body reference: XPath filter + C14N 1.1, xmlenc#sha256 digest.
        var documentReference = new Reference
        {
            Uri = string.Empty,
            DigestMethod = XmlEncSha256Uri
        };
        documentReference.Id = "invoiceSignedData";
        documentReference.AddTransform(CreateXPathTransform(
            "not(ancestor-or-self::*[local-name()='UBLExtensions']) and " +
            "not(ancestor-or-self::*[local-name()='Signature' and namespace-uri()='urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2']) and " +
            "not(ancestor-or-self::*[local-name()='AdditionalDocumentReference' and *[local-name()='ID' and .='QR']])"));
        documentReference.AddTransform(new ZatcaXmlDsigC14N11Transform());
        signedXml.AddReference(documentReference);

        if (certificate is not null)
        {
            var keyInfo = new KeyInfo();
            var keyInfoDoc = new XmlDocument { PreserveWhitespace = true };
            keyInfoDoc.LoadXml(
                "<ds:X509Data xmlns:ds=\"http://www.w3.org/2000/09/xmldsig#\">" +
                "<ds:X509Certificate>" + Convert.ToBase64String(certificate.RawData) + "</ds:X509Certificate>" +
                "</ds:X509Data>");
            keyInfo.AddClause(new KeyInfoNode(keyInfoDoc.DocumentElement!));
            signedXml.KeyInfo = keyInfo;
        }

        // ── (2) Hash <xades:SignedProperties> and ── (3) set ds:Reference URI="#xadesSignedProperties" DigestValue:
        // ComputeSignature → BuildDigestedReferences → Reference.UpdateHashValue applies exc-c14n (xml-exc-c14n) then SHA-256 (xmlenc#sha256).
        var signedPropertiesReference = new Reference
        {
            Uri = "#" + signedPropertiesId,
            Type = "http://www.w3.org/2000/09/xmldsig#SignatureProperties",
            DigestMethod = XmlEncSha256Uri
        };
        signedXml.AddReference(signedPropertiesReference);

        // ── (4) Canonicalize <ds:SignedInfo> (C14N 1.1) and ── (5) ECDSA-SHA256 sign → <ds:SignatureValue>.
        signedXml.ComputeSignature();
        var dsSignature = signedXml.GetXml();

        // .NET SignedXml does not always emit ds:Object with xades:QualifyingProperties; ZATCA needs SigningTime.
        EnsureXadesObjectEmbedded(dsSignature, qualifyingPropertiesElement);
        RewriteSignedPropertiesDigestAndResign(dsSignature, ecdsa, signedPropertiesId);

        // Do not rewrite ds: prefixes after ComputeSignature — it changes SignedInfo canonicalization
        // and breaks SignatureValue, SignedProperties digest, and QR tag 6/7 checks.
        NormalizeX509CertificateValue(dsSignature);

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
        DeclareUblSignatureNamespaces(ublDocumentSignatures);

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

    /// <summary>Match ZATCA SDK sample: <c>sig</c>/<c>sac</c>/<c>sbc</c> declared on <c>UBLDocumentSignatures</c>, not on <c>Invoice</c>.</summary>
    private static void DeclareUblSignatureNamespaces(XmlElement ublDocumentSignatures)
    {
        const string sigNs = "urn:oasis:names:specification:ubl:schema:xsd:CommonSignatureComponents-2";
        const string sacNs = "urn:oasis:names:specification:ubl:schema:xsd:SignatureAggregateComponents-2";
        const string sbcNs = "urn:oasis:names:specification:ubl:schema:xsd:SignatureBasicComponents-2";
        ublDocumentSignatures.SetAttribute("xmlns:sig", sigNs);
        ublDocumentSignatures.SetAttribute("xmlns:sac", sacNs);
        ublDocumentSignatures.SetAttribute("xmlns:sbc", sbcNs);
    }

    private static void EnsureXadesObjectEmbedded(XmlElement dsSignature, XmlElement qualifyingPropertiesOnInvoice)
    {
        if (dsSignature.SelectSingleNode(".//*[local-name()='QualifyingProperties']") is not null)
            return;

        var owner = dsSignature.OwnerDocument!;
        var objectEl = owner.CreateElement("ds", "Object", SignedXml.XmlDsigNamespaceUrl);
        objectEl.AppendChild(qualifyingPropertiesOnInvoice.CloneNode(true));
        dsSignature.AppendChild(objectEl);
    }

    private static void NormalizeX509CertificateValue(XmlElement signatureElement)
    {
        var certNodes = signatureElement.SelectNodes("//*[local-name()='X509Certificate']");
        if (certNodes is null) return;

        foreach (XmlNode node in certNodes)
        {
            if (node is not XmlElement certElement) continue;
            certElement.InnerText = Regex.Replace(certElement.InnerText, "\\s+", string.Empty);
        }
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
        signedProperties.SetAttribute("xmlns:xades", XadesNamespace);
        signedProperties.SetAttribute("Id", signedPropertiesId);

        var signedSignatureProperties = document.CreateElement(
            "xades", "SignedSignatureProperties", XadesNamespace);

        var signingTime = document.CreateElement("xades", "SigningTime", XadesNamespace);
        signingTime.InnerText = FormatSigningTimeMatchingInvoiceAndQr(document);
        signedSignatureProperties.AppendChild(signingTime);

        if (certificate is not null)
        {
            var signingCertificate = document.CreateElement("xades", "SigningCertificate", XadesNamespace);
            var cert = document.CreateElement("xades", "Cert", XadesNamespace);
            var certDigest = document.CreateElement("xades", "CertDigest", XadesNamespace);

            var digestMethod = document.CreateElement("ds", "DigestMethod", SignedXml.XmlDsigNamespaceUrl);
            digestMethod.SetAttribute("Algorithm", XmlEncSha256Uri);

            var digestValue = document.CreateElement("ds", "DigestValue", SignedXml.XmlDsigNamespaceUrl);
            var certificateBase64Body = Convert.ToBase64String(certificate.RawData);
            digestValue.InnerText = HashUtility.ZatcaCertificateBase64BodyDigestValueBase64(certificateBase64Body);

            certDigest.AppendChild(digestMethod);
            certDigest.AppendChild(digestValue);

            var issuerSerial = document.CreateElement("xades", "IssuerSerial", XadesNamespace);
            var issuerName = document.CreateElement("ds", "X509IssuerName", SignedXml.XmlDsigNamespaceUrl);
            issuerName.InnerText = certificate.IssuerName.Name?.Replace(", ", ",") ?? string.Empty;

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

    /// <summary>Match QR Tag 3 / issue fields: <c>yyyy-MM-ddTHH:mm:ssZ</c> UTC (ZATCA certificate-signing-time-format).</summary>
    private static string FormatSigningTimeMatchingInvoiceAndQr(XmlDocument invoiceDocument)
    {
        var root = invoiceDocument.DocumentElement;
        if (root is null)
            return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        var nsmgr = new XmlNamespaceManager(invoiceDocument.NameTable);
        nsmgr.AddNamespace("cbc", "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2");

        var dateStr = root.SelectSingleNode(".//cbc:IssueDate", nsmgr)?.InnerText.Trim();
        var timeStr = root.SelectSingleNode(".//cbc:IssueTime", nsmgr)?.InnerText.Trim();

        if (string.IsNullOrEmpty(dateStr) || string.IsNullOrEmpty(timeStr))
            return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        var dot = timeStr.IndexOf('.');
        if (dot >= 0)
            timeStr = timeStr[..dot];

        return $"{dateStr}T{timeStr}Z";
    }

    private static string ToDecimalSerialNumber(X509Certificate2 certificate)
    {
        return BigInteger.Parse("0" + certificate.SerialNumber, NumberStyles.HexNumber).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// ZATCA signing guide hashes SignedProperties as SHA-256 hex, then Base64 over that hex text.
    /// SignedXml computes standard XMLDSig digest, so we override the digest and re-sign SignedInfo.
    /// </summary>
    private static void RewriteSignedPropertiesDigestAndResign(
        XmlElement signatureElement,
        ECDsa signingKey,
        string signedPropertiesId)
    {
        var signedProperties = signatureElement.SelectSingleNode(
            ".//*[local-name()='SignedProperties' and @Id='" + signedPropertiesId + "']") as XmlElement;
        if (signedProperties is null)
            throw new InvalidOperationException("xades:SignedProperties was not found in signature.");

        // ZATCA guide hashes a standalone populated SignedProperties tag.
        // Build that exact input shape (independent of surrounding signature namespace context).
        var signingTime = signedProperties.SelectSingleNode(
            ".//*[local-name()='SigningTime']")?.InnerText.Trim() ?? string.Empty;
        var certDigestValue = signedProperties.SelectSingleNode(
            ".//*[local-name()='CertDigest']/*[local-name()='DigestValue']")?.InnerText.Trim() ?? string.Empty;
        var issuerName = signedProperties.SelectSingleNode(
            ".//*[local-name()='IssuerSerial']/*[local-name()='X509IssuerName']")?.InnerText.Trim() ?? string.Empty;
        var serialNumber = signedProperties.SelectSingleNode(
            ".//*[local-name()='IssuerSerial']/*[local-name()='X509SerialNumber']")?.InnerText.Trim() ?? string.Empty;

        var digestInputXml =
            "<xades:SignedProperties xmlns:xades=\"http://uri.etsi.org/01903/v1.3.2#\" Id=\"" + signedPropertiesId + "\">" +
            "<xades:SignedSignatureProperties>" +
            "<xades:SigningTime>" + SecurityElement.Escape(signingTime) + "</xades:SigningTime>" +
            "<xades:SigningCertificate>" +
            "<xades:Cert>" +
            "<xades:CertDigest>" +
            "<ds:DigestMethod xmlns:ds=\"http://www.w3.org/2000/09/xmldsig#\" Algorithm=\"http://www.w3.org/2001/04/xmlenc#sha256\"/>" +
            "<ds:DigestValue xmlns:ds=\"http://www.w3.org/2000/09/xmldsig#\">" + SecurityElement.Escape(certDigestValue) + "</ds:DigestValue>" +
            "</xades:CertDigest>" +
            "<xades:IssuerSerial>" +
            "<ds:X509IssuerName xmlns:ds=\"http://www.w3.org/2000/09/xmldsig#\">" + SecurityElement.Escape(issuerName) + "</ds:X509IssuerName>" +
            "<ds:X509SerialNumber xmlns:ds=\"http://www.w3.org/2000/09/xmldsig#\">" + SecurityElement.Escape(serialNumber) + "</ds:X509SerialNumber>" +
            "</xades:IssuerSerial>" +
            "</xades:Cert>" +
            "</xades:SigningCertificate>" +
            "</xades:SignedSignatureProperties>" +
            "</xades:SignedProperties>";

        var signedPropsBytes = Encoding.UTF8.GetBytes(digestInputXml);
        var signedPropsDigestHex = Convert.ToHexString(SHA256.HashData(signedPropsBytes)).ToLowerInvariant();
        var signedPropsDigestBase64OfHex = Convert.ToBase64String(Encoding.ASCII.GetBytes(signedPropsDigestHex));

        var digestValueNode = signatureElement.SelectSingleNode(
            ".//*[local-name()='Reference' and @URI='#" + signedPropertiesId + "']/*[local-name()='DigestValue']") as XmlElement;
        if (digestValueNode is null)
            throw new InvalidOperationException("SignedProperties ds:Reference DigestValue was not found.");
        digestValueNode.InnerText = signedPropsDigestBase64OfHex;

        var signedInfo = signatureElement.SelectSingleNode("./*[local-name()='SignedInfo']") as XmlElement;
        if (signedInfo is null)
            throw new InvalidOperationException("ds:SignedInfo was not found.");
        var signedInfoCanonical = CanonicalizeElementWithC14N11(signedInfo);
        var signatureBytes = signingKey.SignData(signedInfoCanonical, HashAlgorithmName.SHA256);

        var signatureValueNode = signatureElement.SelectSingleNode("./*[local-name()='SignatureValue']") as XmlElement;
        if (signatureValueNode is null)
            throw new InvalidOperationException("ds:SignatureValue was not found.");
        signatureValueNode.InnerText = Convert.ToBase64String(signatureBytes);
    }

    private static byte[] CanonicalizeElementWithC14N11(XmlElement element)
    {
        ZatcaXmlDsigC14N11Transform.EnsureCryptoRegistered();
        var tempDoc = new XmlDocument { PreserveWhitespace = true };
        tempDoc.AppendChild(tempDoc.ImportNode(element, true));

        var transform = new ZatcaXmlDsigC14N11Transform();
        transform.LoadInput(tempDoc);
        using var stream = (Stream)transform.GetOutput(typeof(Stream));
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Places xades:QualifyingProperties in the same invoice tree used later in final output so
    /// URI="#xadesSignedProperties" resolves during SignedXml.ComputeSignature.
    /// </summary>
    private static void AttachQualifyingPropertiesInUblExtensionSignatureShell(
        XmlDocument document,
        XmlElement qualifyingProperties)
    {
        var ns = CreateNamespaceManager(document.NameTable);
        var extensionContent = document.SelectSingleNode(
            "/*/ext:UBLExtensions/ext:UBLExtension/ext:ExtensionContent", ns) as XmlElement;

        if (extensionContent is null)
            throw new InvalidOperationException("UBL ExtensionContent node was not found.");

        extensionContent.RemoveAll();

        var ublDocumentSignatures = document.CreateElement(
            "sig", "UBLDocumentSignatures",
            "urn:oasis:names:specification:ubl:schema:xsd:CommonSignatureComponents-2");
        DeclareUblSignatureNamespaces(ublDocumentSignatures);

        var signatureInformation = document.CreateElement(
            "sac", "SignatureInformation",
            "urn:oasis:names:specification:ubl:schema:xsd:SignatureAggregateComponents-2");

        var signatureShell = document.CreateElement("ds", "Signature", SignedXml.XmlDsigNamespaceUrl);
        var objectEl = document.CreateElement("ds", "Object", SignedXml.XmlDsigNamespaceUrl);
        objectEl.AppendChild(qualifyingProperties);
        signatureShell.AppendChild(objectEl);
        signatureInformation.AppendChild(signatureShell);
        ublDocumentSignatures.AppendChild(signatureInformation);
        extensionContent.AppendChild(ublDocumentSignatures);
    }

    private static byte[] ExtractCertificateSignatureBytes(byte[] certificateDer)
    {
        var reader = new AsnReader(certificateDer, AsnEncodingRules.DER);
        var certSequence = reader.ReadSequence();
        certSequence.ReadEncodedValue(); // tbsCertificate
        certSequence.ReadEncodedValue(); // signatureAlgorithm
        var signature = certSequence.ReadBitString(out _);
        certSequence.ThrowIfNotEmpty();
        reader.ThrowIfNotEmpty();
        return signature;
    }

    private static XmlDsigXPathTransform CreateXPathTransform(string xpathExpression)
    {
        var transform = new XmlDsigXPathTransform();
        var xpathDoc = new XmlDocument { PreserveWhitespace = true };
        xpathDoc.LoadXml(
            "<XPath xmlns=\"http://www.w3.org/2000/09/xmldsig#\" " +
            "xmlns:ext=\"urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2\" " +
            "xmlns:cac=\"urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2\" " +
            "xmlns:cbc=\"urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2\">" +
            xpathExpression +
            "</XPath>");
        transform.LoadInnerXml(xpathDoc.ChildNodes);
        return transform;
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
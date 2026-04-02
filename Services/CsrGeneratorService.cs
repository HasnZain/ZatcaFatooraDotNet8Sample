//using System.Formats.Asn1;
//using System.Globalization;
//using System.Security.Cryptography;
//using System.Security.Cryptography.X509Certificates;
//using System.Text;
//using System.Text.RegularExpressions;
//using ZatcaFatooraDotNet8Sample.Models.Requests;

//namespace ZatcaFatooraDotNet8Sample.Services;

//public sealed class CsrGeneratorService
//{
//    private const string Secp256k1Oid = "1.3.132.0.10";

//    private const string CertificateTemplateNameOid = "1.3.6.1.4.1.311.20.2";
//    private const string SubjectAltNameOid = "2.5.29.17";

//    private const string CountryOid = "2.5.4.6";
//    private const string OrganizationOid = "2.5.4.10";
//    private const string OrganizationalUnitOid = "2.5.4.11";
//    private const string CommonNameOid = "2.5.4.3";
//    private const string OrganizationIdentifierOid = "2.5.4.97";

//    private const string SerialNumberOid = "2.5.4.5";
//    private const string UserIdOid = "0.9.2342.19200300.100.1.1";
//    private const string TitleOid = "2.5.4.12";
//    private const string RegisteredAddressOid = "2.5.4.26";
//    private const string BusinessCategoryOid = "2.5.4.15";

//    private const string ComplianceTemplateName = "PREZATCA-Code-Signing";

//    public sealed record CsrIdentity(
//        string CountryCode,
//        string OrganizationName,
//        string OrganizationalUnitName,
//        string CommonName,
//        string DeviceSerialNumber,
//        string OrganizationIdentifier,
//        string InvoiceType,
//        string Address,
//        string BusinessCategory
//    );

//    public sealed record GeneratedCsr(
//        string CsrPem,
//        string CsrBase64,
//        string PrivateKeyPem,
//        string PublicKeyPem,
//        string SubjectDebug
//    );

//    public GeneratedCsr Generate(GenerateCsrRequest request)
//    {
//        ArgumentNullException.ThrowIfNull(request);

//        var input = MapToIdentity(request);
//        var id = Normalize(input);

//        using var ecdsa = ECDsa.Create(ECCurve.CreateFromValue(Secp256k1Oid));

//        var certificateRequest = new CertificateRequest(
//            BuildSubjectName(id),
//            ecdsa,
//            HashAlgorithmName.SHA256);

//        certificateRequest.CertificateExtensions.Add(BuildCertificateTemplateNameExtension());
//        certificateRequest.CertificateExtensions.Add(BuildSubjectAlternativeNameExtension(id));

//        var csrDer = certificateRequest.CreateSigningRequest();
//        var csrPem = new string(PemEncoding.Write("CERTIFICATE REQUEST", csrDer));

//        var privateKeyDer = ecdsa.ExportPkcs8PrivateKey();
//        var privateKeyPem = new string(PemEncoding.Write("PRIVATE KEY", privateKeyDer));

//        var publicKeyDer = ecdsa.ExportSubjectPublicKeyInfo();
//        var publicKeyPem = new string(PemEncoding.Write("PUBLIC KEY", publicKeyDer));

//        // ZATCA compliance request expects Base64 of the PEM CSR text, not Base64 of raw DER.
//        var csrBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(csrPem));

//        return new GeneratedCsr(
//            CsrPem: csrPem,
//            CsrBase64: csrBase64,
//            PrivateKeyPem: privateKeyPem,
//            PublicKeyPem: publicKeyPem,
//            SubjectDebug: BuildDebugString(id)
//        );
//    }

//    private static CsrIdentity MapToIdentity(GenerateCsrRequest request)
//    {
//        return new CsrIdentity(
//            CountryCode: request.CountryCode,
//            OrganizationName: request.OrganizationName,
//            OrganizationalUnitName: request.OrganizationalUnitName,
//            CommonName: request.CommonName,
//            DeviceSerialNumber: request.SerialNumber,
//            OrganizationIdentifier: request.OrganizationIdentifier,
//            InvoiceType: request.InvoiceType,
//            Address: request.LocationAddress,
//            BusinessCategory: request.IndustryBusinessCategory
//        );
//    }

//    private static CsrIdentity Normalize(CsrIdentity input)
//    {
//        var organizationIdentifier = NormalizeOrganizationIdentifier(input.OrganizationIdentifier);

//        return new CsrIdentity(
//            CountryCode: NormalizeCountryCode(input.CountryCode),
//            OrganizationName: NormalizeGeneralText(input.OrganizationName),
//            OrganizationalUnitName: NormalizeOrganizationalUnitName(
//                organizationIdentifier,
//                input.OrganizationalUnitName),
//            CommonName: NormalizeGeneralText(input.CommonName),
//            DeviceSerialNumber: NormalizeSerialNumber(input.DeviceSerialNumber),
//            OrganizationIdentifier: organizationIdentifier,
//            InvoiceType: NormalizeInvoiceType(input.InvoiceType),
//            Address: NormalizeAddress(input.Address),
//            BusinessCategory: NormalizeGeneralText(input.BusinessCategory)
//        );
//    }

//    private static X500DistinguishedName BuildSubjectName(CsrIdentity id)
//    {
//        var writer = new AsnWriter(AsnEncodingRules.DER);

//        writer.PushSequence();

//        WriteRdn(writer, CountryOid, id.CountryCode, UniversalTagNumber.PrintableString);
//        WriteRdn(writer, OrganizationOid, id.OrganizationName, UniversalTagNumber.UTF8String);
//        WriteRdn(writer, OrganizationalUnitOid, id.OrganizationalUnitName, UniversalTagNumber.UTF8String);
//        WriteRdn(writer, CommonNameOid, id.CommonName, UniversalTagNumber.UTF8String);
//        WriteRdn(writer, OrganizationIdentifierOid, id.OrganizationIdentifier, UniversalTagNumber.UTF8String);

//        writer.PopSequence();

//        return new X500DistinguishedName(writer.Encode());
//    }

//    private static X509Extension BuildCertificateTemplateNameExtension()
//    {
//        var writer = new AsnWriter(AsnEncodingRules.DER);
//        writer.WriteCharacterString(UniversalTagNumber.PrintableString, ComplianceTemplateName);

//        return new X509Extension(
//            new Oid(CertificateTemplateNameOid, "certificateTemplateName"),
//            writer.Encode(),
//            critical: false);
//    }

//    private static X509Extension BuildSubjectAlternativeNameExtension(CsrIdentity id)
//    {
//        var directoryNameBytes = BuildSubjectAlternativeDirectoryName(id);

//        var writer = new AsnWriter(AsnEncodingRules.DER);
//        writer.PushSequence();

//        var directoryNameTag = new Asn1Tag(TagClass.ContextSpecific, 4, isConstructed: true);
//        writer.PushSequence(directoryNameTag);
//        writer.WriteEncodedValue(directoryNameBytes);
//        writer.PopSequence(directoryNameTag);

//        writer.PopSequence();

//        return new X509Extension(
//            new Oid(SubjectAltNameOid, "subjectAltName"),
//            writer.Encode(),
//            critical: false);
//    }

//    private static byte[] BuildSubjectAlternativeDirectoryName(CsrIdentity id)
//    {
//        var writer = new AsnWriter(AsnEncodingRules.DER);

//        writer.PushSequence();

//        WriteRdn(writer, SerialNumberOid, id.DeviceSerialNumber, UniversalTagNumber.UTF8String);
//        WriteRdn(writer, UserIdOid, id.OrganizationIdentifier, UniversalTagNumber.UTF8String);
//        WriteRdn(writer, TitleOid, id.InvoiceType, UniversalTagNumber.UTF8String);
//        WriteRdn(writer, RegisteredAddressOid, id.Address, UniversalTagNumber.UTF8String);
//        WriteRdn(writer, BusinessCategoryOid, id.BusinessCategory, UniversalTagNumber.UTF8String);

//        writer.PopSequence();

//        return writer.Encode();
//    }

//    private static void WriteRdn(
//        AsnWriter writer,
//        string oid,
//        string value,
//        UniversalTagNumber stringType)
//    {
//        writer.PushSetOf();
//        writer.PushSequence();
//        writer.WriteObjectIdentifier(oid);
//        writer.WriteCharacterString(stringType, value);
//        writer.PopSequence();
//        writer.PopSetOf();
//    }

//    private static string BuildDebugString(CsrIdentity id)
//    {
//        return string.Join(", ",
//            $"C={id.CountryCode}",
//            $"O={id.OrganizationName}",
//            $"OU={id.OrganizationalUnitName}",
//            $"CN={id.CommonName}",
//            $"OID.2.5.4.97={id.OrganizationIdentifier}",
//            $"SAN.SN={id.DeviceSerialNumber}",
//            $"SAN.UID={id.OrganizationIdentifier}",
//            $"SAN.TITLE={id.InvoiceType}",
//            $"SAN.registeredAddress={id.Address}",
//            $"SAN.businessCategory={id.BusinessCategory}"
//        );
//    }

//    private static string NormalizeCountryCode(string value)
//    {
//        var s = NormalizeGeneralText(value).ToUpperInvariant();

//        if (s != "SA")
//            throw new ArgumentException("CountryCode must be SA.");

//        return s;
//    }

//    private static string NormalizeOrganizationIdentifier(string value)
//    {
//        var s = NormalizeDigitsOnly(value);

//        if (!Regex.IsMatch(s, @"^3\d{13}3$"))
//            throw new ArgumentException("OrganizationIdentifier must be 15 digits, start with 3, and end with 3.");

//        return s;
//    }

//    private static string NormalizeInvoiceType(string value)
//    {
//        var s = NormalizeDigitsOnly(value);

//        if (!Regex.IsMatch(s, @"^[01]{4}$") || s == "0000")
//            throw new ArgumentException("InvoiceType must be a 4-digit binary value like 1000, 0100, or 1100 and cannot be 0000.");

//        return s;
//    }

//    private static string NormalizeOrganizationalUnitName(string organizationIdentifier, string value)
//    {
//        var s = NormalizeGeneralText(value);
//        var isVatGroup = organizationIdentifier[10] == '1';

//        if (isVatGroup)
//        {
//            var digitsOnly = new string(s.Where(char.IsDigit).ToArray());

//            if (!Regex.IsMatch(digitsOnly, @"^\d{10}$"))
//                throw new ArgumentException("For VAT groups, OrganizationalUnitName must be the 10-digit TIN of the member being onboarded.");

//            return digitsOnly;
//        }

//        return s;
//    }

//    private static string NormalizeDigitsOnly(string value)
//    {
//        var s = RemoveHiddenCharacters(value);
//        s = new string(s.Where(char.IsDigit).ToArray());

//        if (string.IsNullOrWhiteSpace(s))
//            throw new ArgumentException("Expected digits-only value.");

//        return s;
//    }

//    private static string NormalizeSerialNumber(string value)
//    {
//        var s = RemoveHiddenCharacters(value);
//        s = s.Normalize(NormalizationForm.FormC).Trim();

//        s = s.Replace("｜", "|")
//             .Replace("–", "-")
//             .Replace("—", "-")
//             .Replace("−", "-");

//        s = Regex.Replace(s, @"\s*\|\s*", "|");
//        s = Regex.Replace(s, @"\s+", " ").Trim();

//        if (!Regex.IsMatch(s, @"^1-.+\|2-.+\|3-.+$"))
//            throw new ArgumentException("SerialNumber must match the format 1-...|2-...|3-....");

//        return s;
//    }

//    private static string NormalizeAddress(string value)
//    {
//        var s = NormalizeGeneralText(value);

//        s = s.Replace('،', ',')
//             .Replace('؛', ';')
//             .Replace('٫', '.')
//             .Replace('٬', ',');

//        s = Regex.Replace(s, @"\s*,\s*", ", ");
//        s = Regex.Replace(s, @"\s*;\s*", "; ");
//        s = Regex.Replace(s, @"\s+", " ").Trim(' ', ',', ';');

//        if (string.IsNullOrWhiteSpace(s))
//            throw new ArgumentException("LocationAddress is required.");

//        return s;
//    }

//    private static string NormalizeGeneralText(string value)
//    {
//        var s = RemoveHiddenCharacters(value);
//        s = s.Normalize(NormalizationForm.FormC);
//        s = s.Trim();
//        s = Regex.Replace(s, @"\s+", " ");

//        if (string.IsNullOrWhiteSpace(s))
//            throw new ArgumentException("Required value is missing.");

//        return s;
//    }

//    private static string RemoveHiddenCharacters(string value)
//    {
//        if (value is null)
//            return string.Empty;

//        var sb = new StringBuilder(value.Length);

//        foreach (var ch in value)
//        {
//            var category = CharUnicodeInfo.GetUnicodeCategory(ch);

//            var isHiddenFormatting =
//                category == UnicodeCategory.Control ||
//                category == UnicodeCategory.Format ||
//                category == UnicodeCategory.Surrogate ||
//                category == UnicodeCategory.OtherNotAssigned;

//            if (!isHiddenFormatting)
//                sb.Append(ch);
//        }

//        return sb.ToString();
//    }
//}


using System.Formats.Asn1;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using ZatcaFatooraDotNet8Sample.Models.Requests;

namespace ZatcaFatooraDotNet8Sample.Services;

public sealed class CsrGeneratorService
{
    private const string Secp256k1Oid = "1.3.132.0.10";
    private const string CertificateTemplateNameOid = "1.3.6.1.4.1.311.20.2";
    private const string SubjectAltNameOid = "2.5.29.17";
    private const string ComplianceTemplateName = "ZATCA-Code-Signing";

    private const string CountryOid = "2.5.4.6";
    private const string SerialNumberOid = "2.5.4.4";
    private const string OrganizationOid = "2.5.4.10";
    private const string OrganizationalUnitOid = "2.5.4.11";
    private const string CommonNameOid = "2.5.4.3";
    private const string OrganizationIdentifierOid = "2.5.4.97";
    private const string UserIdOid = "0.9.2342.19200300.100.1.1";
    private const string TitleOid = "2.5.4.12";
    private const string RegisteredAddressOid = "2.5.4.26";
    private const string BusinessCategoryOid = "2.5.4.15";

    public sealed record CsrIdentity(
        string CountryCode,
        string OrganizationName,
        string OrganizationalUnitName,
        string CommonName,
        string DeviceSerialNumber,
        string OrganizationIdentifier,
        string InvoiceType,
        string Address,
        string BusinessCategory
    );

    public sealed record GeneratedCsr(
        string CsrPem,
        string CsrBase64,
        string PrivateKeyPem,
        string PublicKeyPem,
        string SubjectDebug
    );

    public GeneratedCsr Generate(GenerateCsrRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var input = MapToIdentity(request);
        var id = Normalize(input);

        using var ecdsa = ECDsa.Create(ECCurve.CreateFromValue(Secp256k1Oid));

        var certificateRequest = new CertificateRequest(
            BuildSubjectName(id),
            ecdsa,
            HashAlgorithmName.SHA256);

        certificateRequest.CertificateExtensions.Add(BuildCertificateTemplateNameExtension());
        certificateRequest.CertificateExtensions.Add(BuildSubjectAlternativeNameExtension(id));

        var csrDer = certificateRequest.CreateSigningRequest();
        var csrPem = new string(PemEncoding.Write("CERTIFICATE REQUEST", csrDer));

        var privateKeyDer = ecdsa.ExportPkcs8PrivateKey();
        var privateKeyPem = new string(PemEncoding.Write("PRIVATE KEY", privateKeyDer));

        var publicKeyDer = ecdsa.ExportSubjectPublicKeyInfo();
        var publicKeyPem = new string(PemEncoding.Write("PUBLIC KEY", publicKeyDer));

        var csrBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(csrPem));

        return new GeneratedCsr(
            CsrPem: csrPem,
            CsrBase64: csrBase64,
            PrivateKeyPem: privateKeyPem,
            PublicKeyPem: publicKeyPem,
            SubjectDebug: BuildDebugString(id)
        );
    }

    private static CsrIdentity MapToIdentity(GenerateCsrRequest request)
    {
        return new CsrIdentity(
            CountryCode: request.CountryCode,
            OrganizationName: request.OrganizationName,
            OrganizationalUnitName: request.OrganizationalUnitName,
            CommonName: request.CommonName,
            DeviceSerialNumber: request.SerialNumber,
            OrganizationIdentifier: request.OrganizationIdentifier,
            InvoiceType: request.InvoiceType,
            Address: request.LocationAddress,
            BusinessCategory: request.IndustryBusinessCategory
        );
    }

    private static CsrIdentity Normalize(CsrIdentity input)
    {
        var organizationIdentifier = NormalizeOrganizationIdentifier(input.OrganizationIdentifier);

        return new CsrIdentity(
            CountryCode: NormalizeCountryCode(input.CountryCode),
            OrganizationName: NormalizeGeneralText(input.OrganizationName),
            OrganizationalUnitName: NormalizeOrganizationalUnitName(
                organizationIdentifier,
                input.OrganizationalUnitName),
            CommonName: NormalizeGeneralText(input.CommonName),
            DeviceSerialNumber: NormalizeSerialNumber(input.DeviceSerialNumber),
            OrganizationIdentifier: organizationIdentifier,
            InvoiceType: NormalizeInvoiceType(input.InvoiceType),
            Address: NormalizeAddress(input.Address),
            BusinessCategory: NormalizeGeneralText(input.BusinessCategory)
        );
    }

    private static X500DistinguishedName BuildSubjectName(CsrIdentity id)
    {
        // ZATCA subject DN contains exactly 4 fields — as specified in zatca_csr.cnf:
        //   C  = Country (SA)
        //   OU = Organizational Unit
        //   O  = Organization Name
        //   CN = Common Name
        // Device-specific fields (SN, UID, T, registeredAddress, businessCategory)
        // go ONLY into the SubjectAlternativeName extension — NOT here.
        var writer = new AsnWriter(AsnEncodingRules.DER);

        writer.PushSequence();

        WriteRdn(writer, CountryOid, id.CountryCode, UniversalTagNumber.PrintableString);
        WriteRdn(writer, OrganizationalUnitOid, id.OrganizationalUnitName, UniversalTagNumber.UTF8String);
        WriteRdn(writer, OrganizationOid, id.OrganizationName, UniversalTagNumber.UTF8String);
        WriteRdn(writer, CommonNameOid, id.CommonName, UniversalTagNumber.UTF8String);

        writer.PopSequence();

        return new X500DistinguishedName(writer.Encode());
    }

    private static X509Extension BuildCertificateTemplateNameExtension()
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.WriteCharacterString(UniversalTagNumber.PrintableString, ComplianceTemplateName);

        return new X509Extension(
            new Oid(CertificateTemplateNameOid, "certificateTemplateName"),
            writer.Encode(),
            critical: false);
    }

    /// <summary>
    /// Builds the SubjectAlternativeName extension required by ZATCA.
    /// The SAN must carry a single directoryName entry containing:
    ///   SN  = device serial number  (2.5.4.5)
    ///   UID = organization identifier (0.9.2342.19200300.100.1.1)
    ///   T   = invoice type 4-bit string (2.5.4.12)
    ///   RegisteredAddress = physical address (2.5.4.26)
    ///   BusinessCategory  = industry category (2.5.4.15)
    /// </summary>
    private static X509Extension BuildSubjectAlternativeNameExtension(CsrIdentity id)
    {
        // Build the inner Name (sequence of RDNs) that goes inside the directoryName choice.
        var nameWriter = new AsnWriter(AsnEncodingRules.DER);
        nameWriter.PushSequence();
        WriteRdn(nameWriter, SerialNumberOid, id.DeviceSerialNumber, UniversalTagNumber.UTF8String);
        WriteRdn(nameWriter, UserIdOid, id.OrganizationIdentifier, UniversalTagNumber.UTF8String);
        WriteRdn(nameWriter, TitleOid, id.InvoiceType, UniversalTagNumber.UTF8String);
        WriteRdn(nameWriter, RegisteredAddressOid, id.Address, UniversalTagNumber.UTF8String);
        WriteRdn(nameWriter, BusinessCategoryOid, id.BusinessCategory, UniversalTagNumber.UTF8String);
        nameWriter.PopSequence();
        var nameBytes = nameWriter.Encode();

        // Wrap it in GeneralNames :: SEQUENCE { directoryName [4] EXPLICIT Name }
        var sanWriter = new AsnWriter(AsnEncodingRules.DER);
        sanWriter.PushSequence(); // GeneralNames
        // directoryName is CHOICE [4] EXPLICIT
        sanWriter.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 4, isConstructed: true));
        sanWriter.WriteEncodedValue(nameBytes);
        sanWriter.PopSequence(new Asn1Tag(TagClass.ContextSpecific, 4, isConstructed: true));
        sanWriter.PopSequence();

        return new X509Extension(
            new Oid(SubjectAltNameOid, "subjectAltName"),
            sanWriter.Encode(),
            critical: false);
    }

    private static void WriteRdn(
        AsnWriter writer,
        string oid,
        string value,
        UniversalTagNumber stringType)
    {
        writer.PushSetOf();
        writer.PushSequence();
        writer.WriteObjectIdentifier(oid);
        writer.WriteCharacterString(stringType, value);
        writer.PopSequence();
        writer.PopSetOf();
    }

    private static string BuildDebugString(CsrIdentity id)
    {
        return string.Join(", ",
            $"C={id.CountryCode}",
            $"SERIALNUMBER={id.DeviceSerialNumber}",
            $"O={id.OrganizationName}",
            $"OU={id.OrganizationalUnitName}",
            $"CN={id.CommonName}",
            $"OID.2.5.4.97={id.OrganizationIdentifier}",
            $"T={id.InvoiceType}",
            $"REGISTEREDADDRESS={id.Address}",
            $"BUSINESSCATEGORY={id.BusinessCategory}"
        );
    }

    private static string NormalizeCountryCode(string value)
    {
        var s = NormalizeGeneralText(value).ToUpperInvariant();

        if (s != "SA")
            throw new ArgumentException("CountryCode must be SA.");

        return s;
    }

    private static string NormalizeOrganizationIdentifier(string value)
    {
        var s = NormalizeDigitsOnly(value);

        if (!Regex.IsMatch(s, @"^3\d{13}3$"))
            throw new ArgumentException("OrganizationIdentifier must be 15 digits, start with 3, and end with 3.");

        return s;
    }

    private static string NormalizeInvoiceType(string value)
    {
        var s = NormalizeDigitsOnly(value);

        if (!Regex.IsMatch(s, @"^[01]{4}$") || s == "0000")
            throw new ArgumentException("InvoiceType must be a 4-digit binary value like 1000, 0100, or 1100 and cannot be 0000.");

        return s;
    }

    private static string NormalizeOrganizationalUnitName(string organizationIdentifier, string value)
    {
        var s = NormalizeGeneralText(value);
        var isVatGroup = organizationIdentifier[10] == '1';

        if (isVatGroup)
        {
            var digitsOnly = new string(s.Where(char.IsDigit).ToArray());

            if (!Regex.IsMatch(digitsOnly, @"^\d{10}$"))
                throw new ArgumentException("For VAT groups, OrganizationalUnitName must be the 10-digit TIN of the member being onboarded.");

            return digitsOnly;
        }

        return s;
    }

    private static string NormalizeDigitsOnly(string value)
    {
        var s = RemoveHiddenCharacters(value);
        s = new string(s.Where(char.IsDigit).ToArray());

        if (string.IsNullOrWhiteSpace(s))
            throw new ArgumentException("Expected digits-only value.");

        return s;
    }

    private static string NormalizeSerialNumber(string value)
    {
        var s = RemoveHiddenCharacters(value);
        s = s.Normalize(NormalizationForm.FormC).Trim();

        s = s.Replace("｜", "|")
             .Replace("–", "-")
             .Replace("—", "-")
             .Replace("−", "-");

        s = Regex.Replace(s, @"\s*\|\s*", "|");
        s = Regex.Replace(s, @"\s+", " ").Trim();

        if (!Regex.IsMatch(s, @"^1-.+\|2-.+\|3-.+$"))
            throw new ArgumentException("SerialNumber must match the format 1-...|2-...|3-....");

        return s;
    }

    private static string NormalizeAddress(string value)
    {
        var s = NormalizeGeneralText(value);

        s = s.Replace('،', ',')
             .Replace('؛', ';')
             .Replace('٫', '.')
             .Replace('٬', ',');

        s = Regex.Replace(s, @"\s*,\s*", ", ");
        s = Regex.Replace(s, @"\s*;\s*", "; ");
        s = Regex.Replace(s, @"\s+", " ").Trim(' ', ',', ';');

        if (string.IsNullOrWhiteSpace(s))
            throw new ArgumentException("LocationAddress is required.");

        return s;
    }

    private static string NormalizeGeneralText(string value)
    {
        var s = RemoveHiddenCharacters(value);
        s = s.Normalize(NormalizationForm.FormC);
        s = s.Trim();
        s = Regex.Replace(s, @"\s+", " ");

        if (string.IsNullOrWhiteSpace(s))
            throw new ArgumentException("Required value is missing.");

        return s;
    }

    private static string RemoveHiddenCharacters(string value)
    {
        if (value is null)
            return string.Empty;

        var sb = new StringBuilder(value.Length);

        foreach (var ch in value)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);

            var isHiddenFormatting =
                category == UnicodeCategory.Control ||
                category == UnicodeCategory.Format ||
                category == UnicodeCategory.Surrogate ||
                category == UnicodeCategory.OtherNotAssigned;

            if (!isHiddenFormatting)
                sb.Append(ch);
        }

        return sb.ToString();
    }
}
using System.Formats.Asn1;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using ZatcaFatooraDotNet8Sample.Models.Requests;
using ZatcaFatooraDotNet8Sample.Models.Responses;
using ZatcaFatooraDotNet8Sample.Utilities;
using static ZatcaFatooraDotNet8Sample.Services.CsrGeneratorService;

namespace ZatcaFatooraDotNet8Sample.Services;

public sealed class CsrGeneratorService
{
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

        using var ecdsa = ECDsa.Create(ECCurve.CreateFromValue("1.3.132.0.10")); // secp256k1

        var dn = BuildDistinguishedName(id);

        var certRequest = new CertificateRequest(
            dn,
            ecdsa,
            HashAlgorithmName.SHA256);

        var csrDer = certRequest.CreateSigningRequest();
        var csrPem = new string(PemEncoding.Write("CERTIFICATE REQUEST", csrDer));

        var privateKeyDer = ecdsa.ExportPkcs8PrivateKey();
        var privateKeyPem = new string(PemEncoding.Write("PRIVATE KEY", privateKeyDer));

        var publicKeyDer = ecdsa.ExportSubjectPublicKeyInfo();
        var publicKeyPem = new string(PemEncoding.Write("PUBLIC KEY", publicKeyDer));

        return new GeneratedCsr(
            CsrPem: csrPem,
            CsrBase64: Convert.ToBase64String(csrDer),
            PrivateKeyPem: privateKeyPem,
            PublicKeyPem: publicKeyPem,
            SubjectDebug: BuildSubjectDebugString(id)
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

    private static X500DistinguishedName BuildDistinguishedName(CsrIdentity id)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);

        writer.PushSequence();

        WriteRdn(writer, "2.5.4.6", id.CountryCode, UniversalTagNumber.PrintableString);
        WriteRdn(writer, "2.5.4.10", id.OrganizationName, UniversalTagNumber.UTF8String);
        WriteRdn(writer, "2.5.4.11", id.OrganizationalUnitName, UniversalTagNumber.UTF8String);
        WriteRdn(writer, "2.5.4.3", id.CommonName, UniversalTagNumber.UTF8String);
        WriteRdn(writer, "2.5.4.5", id.DeviceSerialNumber, UniversalTagNumber.UTF8String);
        WriteRdn(writer, "2.5.4.97", id.OrganizationIdentifier, UniversalTagNumber.UTF8String);
        WriteRdn(writer, "2.5.4.12", id.InvoiceType, UniversalTagNumber.UTF8String);
        WriteRdn(writer, "2.5.4.9", id.Address, UniversalTagNumber.UTF8String);
        WriteRdn(writer, "2.5.4.15", id.BusinessCategory, UniversalTagNumber.UTF8String);

        writer.PopSequence();

        return new X500DistinguishedName(writer.Encode());
    }

    private static void WriteRdn(
        AsnWriter writer,
        string oid,
        string value,
        UniversalTagNumber tag)
    {
        writer.PushSetOf();
        writer.PushSequence();
        writer.WriteObjectIdentifier(oid);
        writer.WriteCharacterString(tag, value);
        writer.PopSequence();
        writer.PopSetOf();
    }

    private static CsrIdentity Normalize(CsrIdentity input)
    {
        return new CsrIdentity(
            CountryCode: NormalizeCountryCode(input.CountryCode),
            OrganizationName: NormalizeGeneralText(input.OrganizationName),
            OrganizationalUnitName: NormalizeGeneralText(input.OrganizationalUnitName),
            CommonName: NormalizeGeneralText(input.CommonName),
            DeviceSerialNumber: NormalizeSerialNumber(input.DeviceSerialNumber),
            OrganizationIdentifier: NormalizeDigitsOnly(input.OrganizationIdentifier),
            InvoiceType: NormalizeDigitsOnly(input.InvoiceType),
            Address: NormalizeAddress(input.Address),
            BusinessCategory: NormalizeGeneralText(input.BusinessCategory)
        );
    }

    private static string BuildSubjectDebugString(CsrIdentity id)
    {
        return string.Join(", ",
            $"C={id.CountryCode}",
            $"O={id.OrganizationName}",
            $"OU={id.OrganizationalUnitName}",
            $"CN={id.CommonName}",
            $"SERIALNUMBER={id.DeviceSerialNumber}",
            $"OID.2.5.4.97={id.OrganizationIdentifier}",
            $"T={id.InvoiceType}",
            $"STREET={id.Address}",
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
        s = s.Normalize(NormalizationForm.FormC);
        s = s.Trim();

        s = s.Replace("｜", "|")
             .Replace("–", "-")
             .Replace("—", "-")
             .Replace("−", "-");

        s = Regex.Replace(s, @"\s*\|\s*", "|");
        s = Regex.Replace(s, @"\s*-\s*", "-");
        s = Regex.Replace(s, @"\s+", " ");

        if (string.IsNullOrWhiteSpace(s))
            throw new ArgumentException("Serial number is required.");

        return s;
    }

    private static string NormalizeAddress(string value)
    {
        var s = NormalizeGeneralText(value);

        s = s.Replace("،", ", ")
             .Replace("؛", "; ")
             .Replace("٫", ".")
             .Replace("٬", ",");

        s = Regex.Replace(s, @"\s*,\s*", ", ");
        s = Regex.Replace(s, @"\s*;\s*", "; ");
        s = Regex.Replace(s, @"\s+", " ").Trim(' ', ',', ';');

        if (string.IsNullOrWhiteSpace(s))
            throw new ArgumentException("Address is required.");

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
        if (value == null)
            return string.Empty;

        var sb = new StringBuilder(value.Length);

        foreach (var ch in value)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);

            var isHiddenFormatting =
                cat == UnicodeCategory.Control ||
                cat == UnicodeCategory.Format ||
                cat == UnicodeCategory.Surrogate ||
                cat == UnicodeCategory.OtherNotAssigned;

            if (!isHiddenFormatting)
                sb.Append(ch);
        }

        return sb.ToString();
    }



    //public CsrGenerationResponse Generate(GenerateCsrRequest request)
    //{
    //    var csrAddress = NormalizeForCsr(request.LocationAddress);
    //    request = new GenerateCsrRequest
    //    {
    //        CommonName = CleanForDn(request.CommonName),
    //        OrganizationName = CleanForDn(request.OrganizationName),
    //        OrganizationalUnitName = CleanForDn(request.OrganizationalUnitName),
    //        CountryCode = CleanForDn(request.CountryCode)?.ToUpperInvariant(),
    //        //EmailAddress = CleanForDn(request.EmailAddress),
    //        SerialNumber = CleanForDn(request.SerialNumber),
    //        OrganizationIdentifier = CleanForDn(request.OrganizationIdentifier),
    //        InvoiceType = CleanForDn(request.InvoiceType),
    //        LocationAddress = CleanForDn(csrAddress),
    //        IndustryBusinessCategory = CleanForDn(request.IndustryBusinessCategory)
    //    };

    //    var subjectName = SubjectNameBuilder.Build(request);
    //    var distinguishedName = BuildDistinguishedName(request);
    //    using var ecdsa = ECDsa.Create(ECCurve.CreateFromValue("1.3.132.0.10"));
    //    //using var ecdsa = ECDsa.Create(ECCurve.CreateFromFriendlyName("secp256k1"));

    //    var certificateRequest = new CertificateRequest(
    //        distinguishedName,
    //        ecdsa,
    //        HashAlgorithmName.SHA256);

    //    // For first successful onboarding attempt, do NOT add extra custom extensions.
    //    // Remove the Microsoft OIDs and other requested extensions for now.

    //    var csrDer = certificateRequest.CreateSigningRequest();
    //    var csrPem = new string(PemEncoding.Write("CERTIFICATE REQUEST", csrDer));
    //    var csrBase64 = Convert.ToBase64String(csrDer);

    //    return new CsrGenerationResponse
    //    {
    //        SubjectName = subjectName,
    //        PrivateKeyPem = ecdsa.ExportPkcs8PrivateKeyPem(),
    //        PublicKeyPem = ecdsa.ExportSubjectPublicKeyInfoPem(),
    //        CsrPem = csrPem,

    //        // ZATCA onboarding usually expects base64 of the PEM text, not raw DER bytes.
    //        //CsrBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(csrPem))
    //        CsrBase64 = csrBase64
    //    };
    //}

    //private static X500DistinguishedName BuildDistinguishedName(GenerateCsrRequest request)
    //{
    //    var writer = new AsnWriter(AsnEncodingRules.DER);

    //    writer.PushSequence();

    //    AddRdn(writer, "2.5.4.3", request.CommonName, UniversalTagNumber.UTF8String);              // CN
    //    AddRdn(writer, "2.5.4.10", request.OrganizationName, UniversalTagNumber.UTF8String);       // O
    //    AddRdn(writer, "2.5.4.11", request.OrganizationalUnitName, UniversalTagNumber.UTF8String); // OU
    //    AddRdn(writer, "2.5.4.6", request.CountryCode?.ToUpperInvariant(), UniversalTagNumber.PrintableString); // C
    //    //AddRdn(writer, "1.2.840.113549.1.9.1", request.EmailAddress, UniversalTagNumber.IA5String); // emailAddress
    //    AddRdn(writer, "2.5.4.5", request.SerialNumber, UniversalTagNumber.UTF8String);            // serialNumber
    //    AddRdn(writer, "2.5.4.97", request.OrganizationIdentifier, UniversalTagNumber.UTF8String); // organizationIdentifier
    //    AddRdn(writer, "2.5.4.12", request.InvoiceType, UniversalTagNumber.UTF8String);            // title
    //    AddRdn(writer, "2.5.4.9", request.LocationAddress, UniversalTagNumber.UTF8String);         // streetAddress
    //    AddRdn(writer, "2.5.4.15", request.IndustryBusinessCategory, UniversalTagNumber.UTF8String); // businessCategory

    //    writer.PopSequence();

    //    return new X500DistinguishedName(writer.Encode());
    //}

    //private static void AddRdn(
    //    AsnWriter writer,
    //    string oid,
    //    string? value,
    //    UniversalTagNumber stringType)
    //{
    //    var cleaned = CleanForDn(value);
    //    if (string.IsNullOrWhiteSpace(cleaned))
    //        return;

    //    writer.PushSetOf();
    //    writer.PushSequence();
    //    writer.WriteObjectIdentifier(oid);
    //    writer.WriteCharacterString(stringType, cleaned);
    //    writer.PopSequence();
    //    writer.PopSetOf();
    //}

    //private static void AddUtf8ExtensionIfPresent(CertificateRequest request, string oid, string? value)
    //{
    //    if (string.IsNullOrWhiteSpace(value))
    //    {
    //        return;
    //    }

    //    var writer = new AsnWriter(AsnEncodingRules.DER);
    //    writer.WriteCharacterString(UniversalTagNumber.UTF8String, value);
    //    request.CertificateExtensions.Add(new X509Extension(new Oid(oid), writer.Encode(), critical: false));
    //}

    //private static string CleanForDn(string? value)
    //{
    //    if (string.IsNullOrWhiteSpace(value))
    //        return string.Empty;

    //    var normalized = value.Normalize(NormalizationForm.FormKC);
    //    var sb = new StringBuilder(normalized.Length);

    //    foreach (var ch in normalized)
    //    {
    //        var category = CharUnicodeInfo.GetUnicodeCategory(ch);

    //        if (category == UnicodeCategory.Control ||
    //            category == UnicodeCategory.Format ||
    //            category == UnicodeCategory.Surrogate ||
    //            category == UnicodeCategory.OtherNotAssigned)
    //        {
    //            continue;
    //        }

    //        if (ch == '\r' || ch == '\n' || ch == '\t')
    //            continue;

    //        sb.Append(ch);
    //    }

    //    return sb.ToString().Trim();
    //}


    ///// <summary>
    ///// Good for saving/displaying a cleaned address without changing its meaning too much.
    ///// </summary>
    //public static string NormalizeForStorage(string? address, bool convertArabicDigitsToEnglish = false)
    //{
    //    if (string.IsNullOrWhiteSpace(address))
    //        return string.Empty;

    //    var value = address.Normalize(NormalizationForm.FormKC);

    //    value = RemoveInvisibleAndControlChars(value);

    //    if (convertArabicDigitsToEnglish)
    //        value = ConvertArabicDigitsToEnglish(value);

    //    // Unify punctuation variants
    //    value = value
    //        .Replace('،', ',')
    //        .Replace('؛', ';')
    //        .Replace('ـ', ' ')
    //        .Replace('“', '"')
    //        .Replace('”', '"')
    //        .Replace('‘', '\'')
    //        .Replace('’', '\'')
    //        .Replace('–', '-')
    //        .Replace('—', '-');

    //    // Replace line breaks / tabs with spaces
    //    value = value
    //        .Replace("\r", " ")
    //        .Replace("\n", " ")
    //        .Replace("\t", " ");

    //    // Clean spaces around punctuation
    //    value = Regex.Replace(value, @"\s*,\s*", ", ");
    //    value = Regex.Replace(value, @"\s*;\s*", "; ");
    //    value = Regex.Replace(value, @"\s*-\s*", " - ");
    //    value = Regex.Replace(value, @"\s*\/\s*", "/");

    //    // Remove repeated punctuation
    //    value = Regex.Replace(value, @"([,;\-])\1+", "$1");

    //    // Collapse multiple spaces
    //    value = Regex.Replace(value, @"\s{2,}", " ");

    //    return value.Trim(' ', ',', ';', '-');
    //}

    ///// <summary>
    ///// Best for CSR/API fields where hidden chars and formatting issues can break parsing.
    ///// Keeps the address readable but stricter than storage normalization.
    ///// </summary>
    //public static string NormalizeForCsr(string? address, bool convertArabicDigitsToEnglish = false)
    //{
    //    var value = NormalizeForStorage(address, convertArabicDigitsToEnglish);

    //    if (string.IsNullOrWhiteSpace(value))
    //        return string.Empty;

    //    // Remove characters that commonly cause trouble in certificate/API text fields
    //    var sb = new StringBuilder(value.Length);

    //    foreach (var ch in value)
    //    {
    //        var category = CharUnicodeInfo.GetUnicodeCategory(ch);

    //        if (category == UnicodeCategory.Control ||
    //            category == UnicodeCategory.Format ||
    //            category == UnicodeCategory.Surrogate ||
    //            category == UnicodeCategory.OtherNotAssigned)
    //        {
    //            continue;
    //        }

    //        // Keep letters, digits, spaces, and common address punctuation only
    //        if (char.IsLetterOrDigit(ch) ||
    //            char.IsWhiteSpace(ch) ||
    //            ch == ',' ||
    //            ch == '-' ||
    //            ch == '/' ||
    //            ch == '.' ||
    //            ch == '#')
    //        {
    //            sb.Append(ch);
    //        }
    //    }

    //    value = sb.ToString();

    //    // Final whitespace cleanup
    //    value = Regex.Replace(value, @"\s{2,}", " ").Trim();

    //    return value;
    //}

    //private static string RemoveInvisibleAndControlChars(string input)
    //{
    //    var sb = new StringBuilder(input.Length);

    //    foreach (var ch in input)
    //    {
    //        var category = CharUnicodeInfo.GetUnicodeCategory(ch);

    //        if (category == UnicodeCategory.Control ||
    //            category == UnicodeCategory.Format ||
    //            category == UnicodeCategory.Surrogate ||
    //            category == UnicodeCategory.OtherNotAssigned)
    //        {
    //            continue;
    //        }

    //        sb.Append(ch);
    //    }

    //    return sb.ToString();
    //}

    //private static string ConvertArabicDigitsToEnglish(string input)
    //{
    //    var sb = new StringBuilder(input.Length);

    //    foreach (var ch in input)
    //    {
    //        sb.Append(ch switch
    //        {
    //            '٠' => '0',
    //            '١' => '1',
    //            '٢' => '2',
    //            '٣' => '3',
    //            '٤' => '4',
    //            '٥' => '5',
    //            '٦' => '6',
    //            '٧' => '7',
    //            '٨' => '8',
    //            '٩' => '9',
    //            '۰' => '0',
    //            '۱' => '1',
    //            '۲' => '2',
    //            '۳' => '3',
    //            '۴' => '4',
    //            '۵' => '5',
    //            '۶' => '6',
    //            '۷' => '7',
    //            '۸' => '8',
    //            '۹' => '9',
    //            _ => ch
    //        });
    //    }

    //    return sb.ToString();
    //}

}

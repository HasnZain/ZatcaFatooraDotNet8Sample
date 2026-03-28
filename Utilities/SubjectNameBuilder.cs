using System.Globalization;
using System.Text;
using ZatcaFatooraDotNet8Sample.Models.Requests;

public static class SubjectNameBuilder
{
    public static string Build(GenerateCsrRequest request)
    {
        var parts = new List<string>();

        Add(parts, "CN", request.CommonName);
        Add(parts, "O", request.OrganizationName);
        Add(parts, "OU", request.OrganizationalUnitName);
        Add(parts, "C", NormalizeCountryCode(request.CountryCode));
        //Add(parts, "OID.1.2.840.113549.1.9.1", request.EmailAddress);   // emailAddress
        Add(parts, "SERIALNUMBER", request.SerialNumber);
        Add(parts, "OID.2.5.4.97", request.OrganizationIdentifier);      // organizationIdentifier
        Add(parts, "T", request.InvoiceType);                            // title / invoiceType
        Add(parts, "STREET", request.LocationAddress);                   // streetAddress / location
        Add(parts, "OID.2.5.4.15", request.IndustryBusinessCategory);    // businessCategory

        return string.Join(", ", parts);
    }

    private static void Add(List<string> parts, string key, string? value)
    {
        var cleaned = Clean(value);
        if (string.IsNullOrWhiteSpace(cleaned))
            return;

        parts.Add($"{key}={EscapeDistinguishedNameValue(cleaned)}");
    }

    private static string NormalizeCountryCode(string? value)
    {
        var cleaned = Clean(value).ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(cleaned))
            return string.Empty;

        if (cleaned.Length != 2)
            throw new InvalidOperationException("countryCode must be exactly 2 letters, for example: SA");

        return cleaned;
    }

    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Normalize(NormalizationForm.FormKC);
        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);

            if (category == UnicodeCategory.Control ||
                category == UnicodeCategory.Format ||
                category == UnicodeCategory.Surrogate ||
                category == UnicodeCategory.OtherNotAssigned)
            {
                continue;
            }

            if (ch == '\r' || ch == '\n' || ch == '\t')
                continue;

            sb.Append(ch);
        }

        return sb.ToString().Trim();
    }

    private static string EscapeDistinguishedNameValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var sb = new StringBuilder(value.Length + 16);

        for (int i = 0; i < value.Length; i++)
        {
            var ch = value[i];

            // Windows X500 parser is picky about pipe in DN values.
            if (ch == '|')
            {
                sb.Append(@"\7C");
                continue;
            }

            var shouldEscape =
                ch == ',' ||
                ch == '+' ||
                ch == '"' ||
                ch == '\\' ||
                ch == '<' ||
                ch == '>' ||
                ch == ';' ||
                ch == '=';

            if (i == 0 && (ch == ' ' || ch == '#'))
                shouldEscape = true;

            if (i == value.Length - 1 && ch == ' ')
                shouldEscape = true;

            if (shouldEscape)
                sb.Append('\\');

            sb.Append(ch);
        }

        return sb.ToString();
    }

}
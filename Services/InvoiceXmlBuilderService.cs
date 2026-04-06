using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using ZatcaFatooraDotNet8Sample.Models.Configuration;
using ZatcaFatooraDotNet8Sample.Models.Domain;
using ZatcaFatooraDotNet8Sample.Models.Requests;

namespace ZatcaFatooraDotNet8Sample.Services;

public sealed class InvoiceXmlBuilderService
{
    private readonly InvoiceQrService _invoiceQrService;
    private readonly ZatcaSettings _settings;
    private readonly InvoiceSignerResolverService _invoiceSignerResolverService;

    public InvoiceXmlBuilderService(
        InvoiceSignerResolverService invoiceSignerResolverService,
        InvoiceQrService invoiceQrService,
        IOptions<ZatcaSettings> settings)
    {
        _invoiceSignerResolverService = invoiceSignerResolverService;
        _invoiceQrService = invoiceQrService;
        _settings = settings.Value;
    }

    public InvoiceBuildResult Build(GenerateInvoiceRequest request, DeviceState state)
    {
        if (string.IsNullOrWhiteSpace(state.PrivateKeyPem))
            throw new InvalidOperationException("No private key was found. Generate the CSR first.");

        // Always work in UTC so IssueDate/IssueTime and QR Tag 3 always match.
        var issueDateTime = request.IssueDateTime?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        var uuid = Guid.NewGuid().ToString();
        var icv = request.InvoiceCounterValue ?? Math.Max(1, state.LastInvoiceCounterValue + 1);
        var previousInvoiceHashBase64 = request.PreviousInvoiceHashBase64
                                        ?? state.LastInvoiceHashBase64
                                        ?? Convert.ToBase64String(Encoding.UTF8.GetBytes("0"));

        var signer = _invoiceSignerResolverService.Resolve(request.InvoiceCategory);
        var certificateToken = ResolveCertificateToken(state);
        var totals = CalculateTotals(request.Lines);

        // Pass 1: sign without QR
        var xml1 = BuildUnsignedXml(request, uuid, issueDateTime, icv, previousInvoiceHashBase64, string.Empty);
        var signed1 = signer.Sign(xml1, state.PrivateKeyPem, certificateToken);

        // Pass 2: build provisional QR and re-sign
        var qr2 = BuildQr(request, issueDateTime, totals, signed1);
        var xml2 = BuildUnsignedXml(request, uuid, issueDateTime, icv, previousInvoiceHashBase64, qr2);
        var signed2 = signer.Sign(xml2, state.PrivateKeyPem, certificateToken);

        // Pass 3: rebuild QR with the new signature value and do final sign
        // (SignatureValue changes between passes because the XML content changed)
        var qr3 = BuildQr(request, issueDateTime, totals, signed2);
        var xml3 = BuildUnsignedXml(request, uuid, issueDateTime, icv, previousInvoiceHashBase64, qr3);
        var signed3 = signer.Sign(xml3, state.PrivateKeyPem, certificateToken);

        return new InvoiceBuildResult
        {
            InvoiceNumber = request.InvoiceNumber,
            Uuid = uuid,
            InvoiceCounterValue = icv,
            PreviousInvoiceHashBase64 = previousInvoiceHashBase64,
            UnsignedXml = xml3,
            SignedXml = signed3.SignedXml,
            InvoiceHashBase64 = signed3.InvoiceHashBase64,
            SignatureBase64 = signed3.SignatureBase64,
            QrCodeBase64 = qr3,
            InvoiceBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(signed3.SignedXml)),
            TaxExclusiveAmount = totals.TaxExclusiveAmount,
            TaxInclusiveAmount = totals.TaxInclusiveAmount,
            TotalTaxAmount = totals.TotalTaxAmount
        };
    }

    private string BuildQr(
        GenerateInvoiceRequest request,
        DateTimeOffset issueDateTime,
        (decimal LineExtensionAmount, decimal TaxExclusiveAmount, decimal TaxInclusiveAmount, decimal TotalTaxAmount, decimal AllowanceTotalAmount) totals,
        SignedInvoiceArtifacts signed)
    {
        return _invoiceQrService.BuildBase64(
            request.Seller.RegistrationName,
            request.Seller.VatNumber,
            issueDateTime,
            totals.TaxInclusiveAmount,
            totals.TotalTaxAmount,
            signed.InvoiceHashBase64,
            signed.SignatureBase64,
            signed.PublicKeyBase64,
            signed.CertificateSignatureBase64);
    }

    private string BuildUnsignedXml(
        GenerateInvoiceRequest request,
        string uuid,
        DateTimeOffset issueDateTime,
        long icv,
        string previousInvoiceHashBase64,
        string qrCodeBase64)
    {
        XNamespace invoiceNs = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
        XNamespace cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
        XNamespace cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
        XNamespace ext = "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2";
        XNamespace sig = "urn:oasis:names:specification:ubl:schema:xsd:CommonSignatureComponents-2";
        XNamespace sac = "urn:oasis:names:specification:ubl:schema:xsd:SignatureAggregateComponents-2";
        XNamespace sbc = "urn:oasis:names:specification:ubl:schema:xsd:SignatureBasicComponents-2";

        var isSimplified = request.InvoiceCategory.Equals("Simplified", StringComparison.OrdinalIgnoreCase);
        var isStandard = !isSimplified;
        var isCreditNote = request.InvoiceTypeCode == "381";
        var isDebitNote = request.InvoiceTypeCode == "383";
        var isCreditOrDebitNote = isCreditNote || isDebitNote;

        var currencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode)
            ? _settings.DefaultCurrencyCode : request.CurrencyCode;
        var taxCurrencyCode = string.IsNullOrWhiteSpace(request.TaxCurrencyCode)
            ? _settings.DefaultTaxCurrencyCode : request.TaxCurrencyCode;

        // Derive the 7-digit binary InvoiceTypeName flag if not provided.
        string invoiceTypeName;
        if (!string.IsNullOrWhiteSpace(request.InvoiceTypeName))
            invoiceTypeName = request.InvoiceTypeName;
        else if (isSimplified)
            invoiceTypeName = isCreditNote ? "0210000" : isDebitNote ? "0201000" : "0200000";
        else
            invoiceTypeName = isCreditNote ? "0110000" : isDebitNote ? "0101000" : "0100000";

        if (isStandard && request.Buyer is null)
            throw new InvalidOperationException("Buyer is required for standard invoices.");
        if (isCreditOrDebitNote && string.IsNullOrWhiteSpace(request.BillingReferenceId))
            throw new InvalidOperationException("BillingReferenceId is required for credit/debit notes.");
        if (isCreditOrDebitNote && string.IsNullOrWhiteSpace(request.CreditDebitReason))
            throw new InvalidOperationException("CreditDebitReason is required for credit/debit notes.");

        var customerParty = request.Buyer ?? CreateSimplifiedCustomerPlaceholder(request);
        var totals = CalculateTotals(request.Lines);

        var invoice = new XElement(
            invoiceNs + "Invoice",
            new XAttribute(XNamespace.Xmlns + "cbc", cbc),
            new XAttribute(XNamespace.Xmlns + "cac", cac),
            new XAttribute(XNamespace.Xmlns + "ext", ext),
            new XAttribute(XNamespace.Xmlns + "sig", sig),
            new XAttribute(XNamespace.Xmlns + "sac", sac),
            new XAttribute(XNamespace.Xmlns + "sbc", sbc),

            new XElement(ext + "UBLExtensions",
                new XElement(ext + "UBLExtension",
                    new XElement(ext + "ExtensionURI", "urn:oasis:names:specification:ubl:dsig:enveloped:xades"),
                    new XElement(ext + "ExtensionContent"))),

            new XElement(cbc + "UBLVersionID", "2.1"),
            new XElement(cbc + "CustomizationID", "urn:zatca:invoice:1.0"),
            new XElement(cbc + "ProfileID", "reporting:1.0"),
            new XElement(cbc + "ID", request.InvoiceNumber),
            new XElement(cbc + "UUID", uuid),
            new XElement(cbc + "IssueDate", issueDateTime.UtcDateTime.ToString("yyyy-MM-dd")),
            new XElement(cbc + "IssueTime", issueDateTime.UtcDateTime.ToString("HH:mm:ss")),

            new XElement(cbc + "InvoiceTypeCode",
                new XAttribute("name", invoiceTypeName),
                request.InvoiceTypeCode),

            string.IsNullOrWhiteSpace(request.Notes) ? null : new XElement(cbc + "Note", request.Notes),

            new XElement(cbc + "DocumentCurrencyCode", currencyCode),
            new XElement(cbc + "TaxCurrencyCode", taxCurrencyCode),

            isStandard
                ? new XElement(cac + "InvoicePeriod",
                    new XElement(cbc + "StartDate", issueDateTime.UtcDateTime.ToString("yyyy-MM-dd")))
                : null,

            isCreditOrDebitNote
                ? new XElement(cac + "BillingReference",
                    new XElement(cac + "InvoiceDocumentReference",
                        new XElement(cbc + "ID", request.BillingReferenceId)))
                : null,

            BuildAdditionalDocumentReference(cac, cbc, "ICV", icv.ToString(CultureInfo.InvariantCulture), null),
            BuildAdditionalDocumentReference(cac, cbc, "PIH", null, previousInvoiceHashBase64),
            BuildAdditionalDocumentReference(cac, cbc, "QR", null, qrCodeBase64),

            new XElement(cac + "Signature",
                new XElement(cbc + "ID", "urn:oasis:names:specification:ubl:signature:Invoice"),
                new XElement(cbc + "SignatureMethod", "urn:oasis:names:specification:ubl:dsig:enveloped:xades")),

            new XElement(cac + "AccountingSupplierParty", BuildPartyElement(cac, cbc, request.Seller)),
            new XElement(cac + "AccountingCustomerParty", BuildPartyElement(cac, cbc, customerParty)),

            new XElement(cac + "PaymentMeans",
                new XElement(cbc + "PaymentMeansCode", request.PaymentMeansCode),
                isCreditOrDebitNote && !string.IsNullOrWhiteSpace(request.CreditDebitReason)
                    ? new XElement(cbc + "InstructionNote", request.CreditDebitReason)
                    : null),

            // ── TaxTotal rules ────────────────────────────────────────────────────────
            //
            // BOTH simplified and standard invoices must have ONE TaxTotal WITH TaxSubtotals.
            // BR-KSA-EN16931-08: "Only one tax total (BG-22) with tax subtotals must be provided."
            // BR-CO-18: "An Invoice shall at least have one VAT breakdown group (BG-23)."
            //
            // The previous BR-KSA-EN16931-09 warning ("without tax subtotals") was for a
            // different scenario (when two TaxTotal blocks are present). The fix is:
            // always emit exactly ONE TaxTotal WITH TaxSubtotals for both invoice types.
            // A second TaxTotal (no subtotals, in taxCurrencyCode) is only needed when
            // DocumentCurrencyCode != TaxCurrencyCode.
            BuildTaxTotalElements(cac, cbc, currencyCode, taxCurrencyCode, totals, request.Lines),

            new XElement(cac + "LegalMonetaryTotal",
                new XElement(cbc + "LineExtensionAmount",
                    new XAttribute("currencyID", currencyCode), FormatAmount(totals.LineExtensionAmount)),
                new XElement(cbc + "TaxExclusiveAmount",
                    new XAttribute("currencyID", currencyCode), FormatAmount(totals.TaxExclusiveAmount)),
                new XElement(cbc + "TaxInclusiveAmount",
                    new XAttribute("currencyID", currencyCode), FormatAmount(totals.TaxInclusiveAmount)),
                new XElement(cbc + "PayableAmount",
                    new XAttribute("currencyID", currencyCode), FormatAmount(totals.TaxInclusiveAmount))),

            request.Lines.Select((line, index) =>
                new XElement(cac + "InvoiceLine",
                    new XElement(cbc + "ID", (index + 1).ToString(CultureInfo.InvariantCulture)),
                    new XElement(cbc + "InvoicedQuantity",
                        new XAttribute("unitCode", line.UnitCode),
                        line.Quantity.ToString("0.##", CultureInfo.InvariantCulture)),
                    new XElement(cbc + "LineExtensionAmount",
                        new XAttribute("currencyID", currencyCode), FormatAmount(GetLineNetAmount(line))),
                    new XElement(cac + "TaxTotal",
                        new XElement(cbc + "TaxAmount",
                            new XAttribute("currencyID", taxCurrencyCode), FormatAmount(GetLineTaxAmount(line))),
                        new XElement(cbc + "RoundingAmount",
                            new XAttribute("currencyID", currencyCode), FormatAmount(GetLineGrossAmount(line)))),
                    new XElement(cac + "Item",
                        new XElement(cbc + "Name", line.Name),
                        new XElement(cac + "ClassifiedTaxCategory",
                            new XElement(cbc + "ID", line.TaxCategoryCode),
                            new XElement(cbc + "Percent", line.TaxRate.ToString("0.00", CultureInfo.InvariantCulture)),
                            new XElement(cac + "TaxScheme",
                                new XElement(cbc + "ID", "VAT")))),
                    new XElement(cac + "Price",
                        new XElement(cbc + "PriceAmount",
                            new XAttribute("currencyID", currencyCode), FormatPrice(GetNetUnitPrice(line))),
                        new XElement(cbc + "BaseQuantity",
                            new XAttribute("unitCode", line.UnitCode), "1"))))
        );

        var document = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), invoice);
        var utf8NoBom = new UTF8Encoding(false);
        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, utf8NoBom);
        document.Save(writer, SaveOptions.DisableFormatting);
        writer.Flush();
        return utf8NoBom.GetString(stream.ToArray());
    }

    /// <summary>
    /// Emits TaxTotal block(s).
    ///
    /// Rule (applies to BOTH simplified and standard invoices):
    ///   - Always ONE TaxTotal in DocumentCurrencyCode WITH TaxSubtotals (BR-CO-18, BR-KSA-EN16931-08).
    ///   - If TaxCurrencyCode != DocumentCurrencyCode: add a SECOND TaxTotal in TaxCurrencyCode
    ///     WITHOUT subtotals (BR-KSA-EN16931-09 only applies in this multi-currency case).
    /// </summary>
    private static object BuildTaxTotalElements(
        XNamespace cac, XNamespace cbc,
        string currencyCode, string taxCurrencyCode,
        (decimal LineExtensionAmount, decimal TaxExclusiveAmount, decimal TaxInclusiveAmount, decimal TotalTaxAmount, decimal AllowanceTotalAmount) totals,
        List<InvoiceLineItem> lines)
    {
        var taxGroups = lines
            .GroupBy(l => (l.TaxCategoryCode, l.TaxRate))
            .Select(g => (
                CategoryCode: g.Key.TaxCategoryCode,
                TaxRate: g.Key.TaxRate,
                TaxableAmount: Round(g.Sum(GetLineNetAmount)),
                TaxAmount: Round(g.Sum(GetLineTaxAmount))))
            .ToList();

        var primaryTaxTotal = new XElement(cac + "TaxTotal",
            new XElement(cbc + "TaxAmount",
                new XAttribute("currencyID", currencyCode),
                FormatAmount(totals.TotalTaxAmount)),
            taxGroups.Select(g =>
                new XElement(cac + "TaxSubtotal",
                    new XElement(cbc + "TaxableAmount",
                        new XAttribute("currencyID", currencyCode), FormatAmount(g.TaxableAmount)),
                    new XElement(cbc + "TaxAmount",
                        new XAttribute("currencyID", currencyCode), FormatAmount(g.TaxAmount)),
                    new XElement(cac + "TaxCategory",
                        new XElement(cbc + "ID", g.CategoryCode),
                        new XElement(cbc + "Percent", g.TaxRate.ToString("0.00", CultureInfo.InvariantCulture)),
                        new XElement(cac + "TaxScheme",
                            new XElement(cbc + "ID", "VAT"))))));

        if (string.Equals(currencyCode, taxCurrencyCode, StringComparison.OrdinalIgnoreCase))
            return primaryTaxTotal;

        // Multi-currency: second TaxTotal in taxCurrencyCode, no subtotals
        return new object[]
        {
            primaryTaxTotal,
            new XElement(cac + "TaxTotal",
                new XElement(cbc + "TaxAmount",
                    new XAttribute("currencyID", taxCurrencyCode),
                    FormatAmount(totals.TotalTaxAmount)))
        };
    }

    private static PartyInfo CreateSimplifiedCustomerPlaceholder(GenerateInvoiceRequest request)
    {
        if (!request.InvoiceCategory.Equals("Simplified", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Buyer is required for non-simplified invoices.");
        return new PartyInfo
        {
            RegistrationName = "Walk-in Customer",
            CountryCode = string.IsNullOrWhiteSpace(request.Seller?.CountryCode) ? "SA" : request.Seller.CountryCode
        };
    }

    private static XElement BuildPartyElement(XNamespace cac, XNamespace cbc, PartyInfo party)
    {
        return new XElement(cac + "Party",
            string.IsNullOrWhiteSpace(party.RegistrationNumber) ? null
                : new XElement(cac + "PartyIdentification",
                    new XElement(cbc + "ID", new XAttribute("schemeID", "CRN"), party.RegistrationNumber)),

            string.IsNullOrWhiteSpace(party.CountryCode) &&
            string.IsNullOrWhiteSpace(party.StreetName) &&
            string.IsNullOrWhiteSpace(party.BuildingNumber) &&
            string.IsNullOrWhiteSpace(party.PlotIdentification) &&
            string.IsNullOrWhiteSpace(party.CitySubdivisionName) &&
            string.IsNullOrWhiteSpace(party.CityName) &&
            string.IsNullOrWhiteSpace(party.PostalZone)
                ? null
                : new XElement(cac + "PostalAddress",
                    string.IsNullOrWhiteSpace(party.StreetName) ? null : new XElement(cbc + "StreetName", party.StreetName),
                    string.IsNullOrWhiteSpace(party.BuildingNumber) ? null : new XElement(cbc + "BuildingNumber", party.BuildingNumber),
                    string.IsNullOrWhiteSpace(party.PlotIdentification) ? null : new XElement(cbc + "PlotIdentification", party.PlotIdentification),
                    string.IsNullOrWhiteSpace(party.CitySubdivisionName) ? null : new XElement(cbc + "CitySubdivisionName", party.CitySubdivisionName),
                    string.IsNullOrWhiteSpace(party.CityName) ? null : new XElement(cbc + "CityName", party.CityName),
                    string.IsNullOrWhiteSpace(party.PostalZone) ? null : new XElement(cbc + "PostalZone", party.PostalZone),
                    string.IsNullOrWhiteSpace(party.CountryCode) ? null
                        : new XElement(cac + "Country", new XElement(cbc + "IdentificationCode", party.CountryCode))),

            string.IsNullOrWhiteSpace(party.VatNumber) ? null
                : new XElement(cac + "PartyTaxScheme",
                    new XElement(cbc + "CompanyID", party.VatNumber),
                    new XElement(cac + "TaxScheme", new XElement(cbc + "ID", "VAT"))),

            string.IsNullOrWhiteSpace(party.RegistrationName) ? null
                : new XElement(cac + "PartyLegalEntity",
                    new XElement(cbc + "RegistrationName", party.RegistrationName)));
    }

    private static XElement BuildAdditionalDocumentReference(
        XNamespace cac, XNamespace cbc, string id, string? uuid, string? embeddedValue)
    {
        return new XElement(cac + "AdditionalDocumentReference",
            new XElement(cbc + "ID", id),
            string.IsNullOrWhiteSpace(uuid) ? null : new XElement(cbc + "UUID", uuid),
            new XElement(cac + "Attachment",
                new XElement(cbc + "EmbeddedDocumentBinaryObject",
                    new XAttribute("mimeCode", "text/plain"),
                    embeddedValue ?? string.Empty)));
    }

    // ── Calculation helpers ──────────────────────────────────────────────────────────────

    private static (decimal LineExtensionAmount, decimal TaxExclusiveAmount, decimal TaxInclusiveAmount, decimal TotalTaxAmount, decimal AllowanceTotalAmount)
        CalculateTotals(IEnumerable<InvoiceLineItem> lines)
    {
        var lineList = lines.ToList();
        var lineExtensionAmount = Round(lineList.Sum(GetLineNetAmount));
        var totalTaxAmount = Round(lineList.Sum(GetLineTaxAmount));
        var taxExclusiveAmount = lineExtensionAmount;
        var taxInclusiveAmount = Round(taxExclusiveAmount + totalTaxAmount);
        return (lineExtensionAmount, taxExclusiveAmount, taxInclusiveAmount, totalTaxAmount, 0m);
    }

    private static decimal GetLineNetAmount(InvoiceLineItem line)
        => Round(line.Quantity * line.UnitPrice - line.DiscountAmount);

    private static decimal GetLineTaxAmount(InvoiceLineItem line)
        => Round(GetLineNetAmount(line) * (line.TaxRate / 100m));

    private static decimal GetLineGrossAmount(InvoiceLineItem line)
        => Round(GetLineNetAmount(line) + GetLineTaxAmount(line));

    private static decimal GetNetUnitPrice(InvoiceLineItem line)
    {
        if (line.Quantity <= 0)
            throw new InvalidOperationException("Line quantity must be greater than zero.");
        return decimal.Round(GetLineNetAmount(line) / line.Quantity, 6, MidpointRounding.AwayFromZero);
    }

    private static decimal Round(decimal value)
        => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string FormatAmount(decimal value)
        => Round(value).ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatPrice(decimal value)
        => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string? ResolveCertificateToken(DeviceState state)
        => !string.IsNullOrWhiteSpace(state.ProductionBinarySecurityToken)
            ? state.ProductionBinarySecurityToken
            : state.ComplianceBinarySecurityToken;
}
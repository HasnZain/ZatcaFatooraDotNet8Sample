using System.Globalization;
using System.Text;
using System.Xml;
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

        // Single-pass: sign XML with an empty QR payload (QR is excluded from the invoice ds:Reference digest).
        var unsignedXml = BuildUnsignedXml(request, uuid, issueDateTime, icv, previousInvoiceHashBase64, string.Empty);
        var signed = signer.Sign(unsignedXml, state.PrivateKeyPem, certificateToken);

        // QR tags 6–7 use the invoice reference DigestValue and ds:SignatureValue from this signature (see Signer).
        var qrBase64 = BuildQr(request, issueDateTime, totals, signed);
        var signedXmlFinal = ReplaceQrEmbeddedPayload(signed.SignedXml, qrBase64);

        var unsignedXmlWithQr = BuildUnsignedXml(
            request, uuid, issueDateTime, icv, previousInvoiceHashBase64, qrBase64);

        return new InvoiceBuildResult
        {
            InvoiceNumber = request.InvoiceNumber,
            Uuid = uuid,
            InvoiceCounterValue = icv,
            PreviousInvoiceHashBase64 = previousInvoiceHashBase64,
            UnsignedXml = unsignedXmlWithQr,
            SignedXml = signedXmlFinal,
            InvoiceHashBase64 = signed.InvoiceHashBase64,
            SignatureBase64 = signed.SignatureBase64,
            QrCodeBase64 = qrBase64,
            InvoiceBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(signedXmlFinal)),
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

        var totals = CalculateTotals(request.Lines);

        // SDK sample is a normal simplified tax invoice (388): no UBLVersionID/CustomizationID, etc.
        bool simplifiedSdkLayout = isSimplified
            && !isCreditOrDebitNote
            && string.Equals(currencyCode, taxCurrencyCode, StringComparison.OrdinalIgnoreCase);

        var includeUblVersionAndCustomization = !simplifiedSdkLayout;

        var invoice = new XElement(
            invoiceNs + "Invoice",
            new XAttribute(XNamespace.Xmlns + "cac", cac.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "cbc", cbc.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "ext", ext.NamespaceName),

            new XElement(ext + "UBLExtensions",
                new XElement(ext + "UBLExtension",
                    new XElement(ext + "ExtensionURI", "urn:oasis:names:specification:ubl:dsig:enveloped:xades"),
                    new XElement(ext + "ExtensionContent"))),

            includeUblVersionAndCustomization
                ? new XElement(cbc + "UBLVersionID", "2.1")
                : null,
            includeUblVersionAndCustomization
                ? new XElement(cbc + "CustomizationID", "urn:zatca:invoice:1.0")
                : null,

            new XElement(cbc + "ProfileID", "reporting:1.0"),
            new XElement(cbc + "ID", request.InvoiceNumber),
            new XElement(cbc + "UUID", uuid),
            new XElement(cbc + "IssueDate", issueDateTime.UtcDateTime.ToString("yyyy-MM-dd")),
            new XElement(cbc + "IssueTime", issueDateTime.UtcDateTime.ToString("HH:mm:ss")),

            new XElement(cbc + "InvoiceTypeCode",
                new XAttribute("name", invoiceTypeName),
                request.InvoiceTypeCode),

            BuildNoteElement(cbc, request),

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

            (isSimplified && !isCreditOrDebitNote)
                ? new XElement(cac + "AccountingCustomerParty")
                : new XElement(cac + "AccountingCustomerParty",
                    request.Buyer is null ? null : BuildPartyElement(cac, cbc, request.Buyer)),

            new XElement(cac + "PaymentMeans",
                new XElement(cbc + "PaymentMeansCode", request.PaymentMeansCode),
                isCreditOrDebitNote && !string.IsNullOrWhiteSpace(request.CreditDebitReason)
                    ? new XElement(cbc + "InstructionNote", request.CreditDebitReason)
                    : null),

            simplifiedSdkLayout ? BuildSdkStyleAllowanceCharge(cac, cbc, currencyCode) : null,

            simplifiedSdkLayout
                ? BuildSimplifiedSdkStyleTaxTotals(cac, cbc, currencyCode, totals, request.Lines)
                : BuildTaxTotalElements(cac, cbc, currencyCode, taxCurrencyCode, totals, request.Lines),

            simplifiedSdkLayout
                ? BuildSimplifiedSdkLegalMonetaryTotal(cac, cbc, currencyCode, totals)
                : new XElement(cac + "LegalMonetaryTotal",
                    new XElement(cbc + "LineExtensionAmount",
                        new XAttribute("currencyID", currencyCode), FormatAmount(totals.LineExtensionAmount)),
                    new XElement(cbc + "TaxExclusiveAmount",
                        new XAttribute("currencyID", currencyCode), FormatAmount(totals.TaxExclusiveAmount)),
                    new XElement(cbc + "TaxInclusiveAmount",
                        new XAttribute("currencyID", currencyCode), FormatAmount(totals.TaxInclusiveAmount)),
                    new XElement(cbc + "PayableAmount",
                        new XAttribute("currencyID", currencyCode), FormatAmount(totals.TaxInclusiveAmount))),

            request.Lines.Select((line, index) =>
                BuildInvoiceLineElement(cac, cbc, currencyCode, taxCurrencyCode, line, index, simplifiedSdkLayout))
        );

        var document = new XDocument(
            new XDeclaration("1.0", simplifiedSdkLayout ? "UTF-8" : "utf-8", simplifiedSdkLayout ? null : "yes"),
            invoice);
        var utf8NoBom = new UTF8Encoding(false);
        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, utf8NoBom);
        document.Save(writer, SaveOptions.DisableFormatting);
        writer.Flush();
        return utf8NoBom.GetString(stream.ToArray());
    }

    private static XElement? BuildNoteElement(XNamespace cbc, GenerateInvoiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Notes))
            return null;

        var lang = request.NoteLanguageId;
        if (string.IsNullOrWhiteSpace(lang)
            && string.Equals(request.InvoiceCategory, "Simplified", StringComparison.OrdinalIgnoreCase))
            lang = "ar";

        return string.IsNullOrWhiteSpace(lang)
            ? new XElement(cbc + "Note", request.Notes)
            : new XElement(cbc + "Note", new XAttribute("languageID", lang), request.Notes);
    }

    /// <summary>ZATCA Java SDK simplified sample: zero discount allowance with UN/ECE tax categories (duplicated).</summary>
    private static XElement BuildSdkStyleAllowanceCharge(XNamespace cac, XNamespace cbc, string currencyCode)
    {
        XElement taxCategory() => new XElement(cac + "TaxCategory",
            new XElement(cbc + "ID",
                new XAttribute("schemeID", "UN/ECE 5305"),
                new XAttribute("schemeAgencyID", "6"),
                "S"),
            new XElement(cbc + "Percent", "15"),
            new XElement(cac + "TaxScheme",
                new XElement(cbc + "ID",
                    new XAttribute("schemeID", "UN/ECE 5153"),
                    new XAttribute("schemeAgencyID", "6"),
                    "VAT")));

        return new XElement(cac + "AllowanceCharge",
            new XElement(cbc + "ChargeIndicator", "false"),
            new XElement(cbc + "AllowanceChargeReason", "discount"),
            new XElement(cbc + "Amount", new XAttribute("currencyID", currencyCode), "0.00"),
            taxCategory(),
            taxCategory());
    }

    /// <summary>Two <c>cac:TaxTotal</c> blocks as in ZATCA SDK simplified sample (summary, then breakdown).</summary>
    private static object[] BuildSimplifiedSdkStyleTaxTotals(
        XNamespace cac,
        XNamespace cbc,
        string currencyCode,
        (decimal LineExtensionAmount, decimal TaxExclusiveAmount, decimal TaxInclusiveAmount, decimal TotalTaxAmount, decimal AllowanceTotalAmount) totals,
        List<InvoiceLineItem> lines)
    {
        var primaryLine = lines[0];
        var summary = new XElement(cac + "TaxTotal",
            new XElement(cbc + "TaxAmount",
                new XAttribute("currencyID", currencyCode),
                FormatAmount(totals.TotalTaxAmount)));

        var detail = new XElement(cac + "TaxTotal",
            new XElement(cbc + "TaxAmount",
                new XAttribute("currencyID", currencyCode),
                FormatAmount(totals.TotalTaxAmount)),
            new XElement(cac + "TaxSubtotal",
                new XElement(cbc + "TaxableAmount",
                    new XAttribute("currencyID", currencyCode),
                    FormatAmount(totals.TaxExclusiveAmount)),
                new XElement(cbc + "TaxAmount",
                    new XAttribute("currencyID", currencyCode),
                    FormatAmount(totals.TotalTaxAmount)),
                new XElement(cac + "TaxCategory",
                    new XElement(cbc + "ID",
                        new XAttribute("schemeID", "UN/ECE 5305"),
                        new XAttribute("schemeAgencyID", "6"),
                        primaryLine.TaxCategoryCode),
                    new XElement(cbc + "Percent", primaryLine.TaxRate.ToString("0.00", CultureInfo.InvariantCulture)),
                    new XElement(cac + "TaxScheme",
                        new XElement(cbc + "ID",
                            new XAttribute("schemeID", "UN/ECE 5153"),
                            new XAttribute("schemeAgencyID", "6"),
                            "VAT")))));

        return new object[] { summary, detail };
    }

    private static XElement BuildSimplifiedSdkLegalMonetaryTotal(
        XNamespace cac,
        XNamespace cbc,
        string currencyCode,
        (decimal LineExtensionAmount, decimal TaxExclusiveAmount, decimal TaxInclusiveAmount, decimal TotalTaxAmount, decimal AllowanceTotalAmount) totals)
    {
        return new XElement(cac + "LegalMonetaryTotal",
            new XElement(cbc + "LineExtensionAmount",
                new XAttribute("currencyID", currencyCode), FormatAmount(totals.LineExtensionAmount)),
            new XElement(cbc + "TaxExclusiveAmount",
                new XAttribute("currencyID", currencyCode), FormatAmount(totals.TaxExclusiveAmount)),
            new XElement(cbc + "TaxInclusiveAmount",
                new XAttribute("currencyID", currencyCode), FormatAmount(totals.TaxInclusiveAmount)),
            new XElement(cbc + "AllowanceTotalAmount",
                new XAttribute("currencyID", currencyCode), "0.00"),
            new XElement(cbc + "PrepaidAmount",
                new XAttribute("currencyID", currencyCode), "0.00"),
            new XElement(cbc + "PayableAmount",
                new XAttribute("currencyID", currencyCode), FormatAmount(totals.TaxInclusiveAmount)));
    }

    private static XElement BuildInvoiceLineElement(
        XNamespace cac,
        XNamespace cbc,
        string currencyCode,
        string taxCurrencyCode,
        InvoiceLineItem line,
        int index,
        bool simplifiedSdkLayout)
    {
        var id = (index + 1).ToString(CultureInfo.InvariantCulture);
        var qtyFormat = simplifiedSdkLayout ? "0.000000" : "0.##";
        var quantityString = line.Quantity.ToString(qtyFormat, CultureInfo.InvariantCulture);

        var priceEl = simplifiedSdkLayout
            ? new XElement(cac + "Price",
                new XElement(cbc + "PriceAmount",
                    new XAttribute("currencyID", currencyCode),
                    FormatPrice(GetNetUnitPrice(line))))
            : new XElement(cac + "Price",
                new XElement(cbc + "PriceAmount",
                    new XAttribute("currencyID", currencyCode),
                    FormatPrice(GetNetUnitPrice(line))),
                new XElement(cbc + "BaseQuantity",
                    new XAttribute("unitCode", line.UnitCode),
                    "1"));

        return new XElement(cac + "InvoiceLine",
            new XElement(cbc + "ID", id),
            new XElement(cbc + "InvoicedQuantity",
                new XAttribute("unitCode", line.UnitCode),
                quantityString),
            new XElement(cbc + "LineExtensionAmount",
                new XAttribute("currencyID", currencyCode),
                FormatAmount(GetLineNetAmount(line))),
            new XElement(cac + "TaxTotal",
                new XElement(cbc + "TaxAmount",
                    new XAttribute("currencyID", taxCurrencyCode),
                    FormatAmount(GetLineTaxAmount(line))),
                new XElement(cbc + "RoundingAmount",
                    new XAttribute("currencyID", currencyCode),
                    FormatAmount(GetLineGrossAmount(line)))),
            new XElement(cac + "Item",
                new XElement(cbc + "Name", line.Name),
                new XElement(cac + "ClassifiedTaxCategory",
                    new XElement(cbc + "ID", line.TaxCategoryCode),
                    new XElement(cbc + "Percent", line.TaxRate.ToString("0.00", CultureInfo.InvariantCulture)),
                    new XElement(cac + "TaxScheme",
                        new XElement(cbc + "ID", "VAT")))),
            priceEl);
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

        // Emit a second TaxTotal (without subtotals) only for true multi-currency scenarios.
        bool needSecondTaxTotal = !string.Equals(currencyCode, taxCurrencyCode, StringComparison.OrdinalIgnoreCase);

        if (!needSecondTaxTotal)
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
        var isQr = string.Equals(id, "QR", StringComparison.OrdinalIgnoreCase);
        var payload = embeddedValue ?? string.Empty;
        var includeAttachment = isQr || !string.IsNullOrWhiteSpace(embeddedValue);

        return new XElement(cac + "AdditionalDocumentReference",
            new XElement(cbc + "ID", id),
            string.IsNullOrWhiteSpace(uuid) ? null : new XElement(cbc + "UUID", uuid),
            includeAttachment
                ? new XElement(cac + "Attachment",
                    new XElement(cbc + "EmbeddedDocumentBinaryObject",
                        new XAttribute("mimeCode", "text/plain"),
                        payload))
                : null);
    }

    /// <summary>
    /// The invoice digest excludes the QR reference, so updating only the QR payload after signing
    /// does not invalidate the XML signature while aligning TLV tag 7 with <c>ds:SignatureValue</c>.
    /// </summary>
    private static string ReplaceQrEmbeddedPayload(string signedXml, string newQrBase64)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(signedXml);
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("cac", "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2");
        ns.AddNamespace("cbc", "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2");
        var node = doc.SelectSingleNode(
            "//cac:AdditionalDocumentReference[cbc:ID='QR']//cbc:EmbeddedDocumentBinaryObject",
            ns);
        if (node is null)
            throw new InvalidOperationException("Could not find QR EmbeddedDocumentBinaryObject in signed XML.");
        node.InnerText = newQrBase64;
        return doc.OuterXml;
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
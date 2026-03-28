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
    private readonly InvoiceSignerService _invoiceSignerService;
    private readonly InvoiceQrService _invoiceQrService;
    private readonly ZatcaSettings _settings;

    public InvoiceXmlBuilderService(
        InvoiceSignerService invoiceSignerService,
        InvoiceQrService invoiceQrService,
        IOptions<ZatcaSettings> settings)
    {
        _invoiceSignerService = invoiceSignerService;
        _invoiceQrService = invoiceQrService;
        _settings = settings.Value;
    }

    public InvoiceBuildResult Build(GenerateInvoiceRequest request, DeviceState state)
    {
        if (string.IsNullOrWhiteSpace(state.PrivateKeyPem))
        {
            throw new InvalidOperationException("No private key was found. Generate the CSR first.");
        }

        var issueDateTime = request.IssueDateTime?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        var uuid = Guid.NewGuid().ToString();
        var icv = request.InvoiceCounterValue ?? Math.Max(1, state.LastInvoiceCounterValue + 1);
        var previousInvoiceHashBase64 = request.PreviousInvoiceHashBase64
                                        ?? state.LastInvoiceHashBase64
                                        ?? Convert.ToBase64String(Encoding.UTF8.GetBytes("0"));

        var initialXml = BuildUnsignedXml(request, uuid, issueDateTime, icv, previousInvoiceHashBase64, qrCodeBase64: string.Empty);
        var initialSignature = _invoiceSignerService.Sign(initialXml, state.PrivateKeyPem, ResolveCertificateToken(state));

        var totals = CalculateTotals(request.Lines);
        var qrCodeBase64 = _invoiceQrService.BuildBase64(
            request.Seller.RegistrationName,
            request.Seller.VatNumber,
            issueDateTime,
            totals.TaxInclusiveAmount,
            totals.TotalTaxAmount,
            initialSignature.InvoiceHashBase64,
            initialSignature.SignatureBase64,
            initialSignature.PublicKeyBase64,
            initialSignature.CertificateSignatureBase64);

        var unsignedXml = BuildUnsignedXml(request, uuid, issueDateTime, icv, previousInvoiceHashBase64, qrCodeBase64);
        var signed = _invoiceSignerService.Sign(unsignedXml, state.PrivateKeyPem, ResolveCertificateToken(state));

        return new InvoiceBuildResult
        {
            InvoiceNumber = request.InvoiceNumber,
            Uuid = uuid,
            InvoiceCounterValue = icv,
            PreviousInvoiceHashBase64 = previousInvoiceHashBase64,
            UnsignedXml = unsignedXml,
            SignedXml = signed.SignedXml,
            InvoiceHashBase64 = signed.InvoiceHashBase64,
            SignatureBase64 = signed.SignatureBase64,
            QrCodeBase64 = qrCodeBase64,
            InvoiceBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(signed.SignedXml)),
            TaxExclusiveAmount = totals.TaxExclusiveAmount,
            TaxInclusiveAmount = totals.TaxInclusiveAmount,
            TotalTaxAmount = totals.TotalTaxAmount
        };
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

        var currencyCode = string.IsNullOrWhiteSpace(request.CurrencyCode)
            ? _settings.DefaultCurrencyCode
            : request.CurrencyCode;

        var taxCurrencyCode = string.IsNullOrWhiteSpace(request.TaxCurrencyCode)
            ? _settings.DefaultTaxCurrencyCode
            : request.TaxCurrencyCode;

        var totals = CalculateTotals(request.Lines);

        var invoiceTypeName = !string.IsNullOrWhiteSpace(request.InvoiceTypeName)
            ? request.InvoiceTypeName
            : request.InvoiceCategory.Equals("Simplified", StringComparison.OrdinalIgnoreCase)
                ? "0200000"
                : "0100000";

        var invoice = new XElement(
            invoiceNs + "Invoice",
            new XAttribute(XNamespace.Xmlns + "cbc", cbc),
            new XAttribute(XNamespace.Xmlns + "cac", cac),
            new XAttribute(XNamespace.Xmlns + "ext", ext),

            new XElement(
                ext + "UBLExtensions",
                new XElement(
                    ext + "UBLExtension",
                    new XElement(ext + "ExtensionURI", "urn:oasis:names:specification:ubl:dsig:enveloped:xades"),
                    new XElement(ext + "ExtensionContent")
                )
            ),

            new XElement(cbc + "UBLVersionID", "2.1"),
            new XElement(cbc + "CustomizationID", "urn:zatca:invoice:1.0"),
            new XElement(
                cbc + "ProfileID",
                request.InvoiceCategory.Equals("Simplified", StringComparison.OrdinalIgnoreCase)
                    ? "reporting:1.0"
                    : "clearance:1.0"
            ),
            new XElement(cbc + "ID", request.InvoiceNumber),
            new XElement(cbc + "UUID", uuid),
            new XElement(cbc + "IssueDate", issueDateTime.ToString("yyyy-MM-dd")),
            new XElement(cbc + "IssueTime", issueDateTime.ToString("HH:mm:ss")),

            new XElement(
                cbc + "InvoiceTypeCode",
                new XAttribute("name", invoiceTypeName),
                request.InvoiceTypeCode
            ),

            string.IsNullOrWhiteSpace(request.Notes) ? null : new XElement(cbc + "Note", request.Notes),
            new XElement(cbc + "DocumentCurrencyCode", currencyCode),
            new XElement(cbc + "TaxCurrencyCode", taxCurrencyCode),

            BuildAdditionalDocumentReference(cac, cbc, "ICV", icv.ToString(CultureInfo.InvariantCulture), null),
            BuildAdditionalDocumentReference(cac, cbc, "PIH", null, previousInvoiceHashBase64),
            BuildAdditionalDocumentReference(cac, cbc, "QR", null, qrCodeBase64),

            new XElement(cac + "AccountingSupplierParty", BuildPartyElement(cac, cbc, request.Seller)),
            request.Buyer is null
                ? null
                : new XElement(cac + "AccountingCustomerParty", BuildPartyElement(cac, cbc, request.Buyer)),

            new XElement(
                cac + "PaymentMeans",
                new XElement(cbc + "PaymentMeansCode", request.PaymentMeansCode)
            ),

            new XElement(
                cac + "TaxTotal",
                new XElement(
                    cbc + "TaxAmount",
                    new XAttribute("currencyID", taxCurrencyCode),
                    FormatAmount(totals.TotalTaxAmount)
                ),
                new XElement(
                    cac + "TaxSubtotal",
                    new XElement(
                        cbc + "TaxableAmount",
                        new XAttribute("currencyID", currencyCode),
                        FormatAmount(totals.TaxExclusiveAmount)
                    ),
                    new XElement(
                        cbc + "TaxAmount",
                        new XAttribute("currencyID", taxCurrencyCode),
                        FormatAmount(totals.TotalTaxAmount)
                    ),
                    new XElement(
                        cac + "TaxCategory",
                        new XElement(cbc + "ID", request.Lines.First().TaxCategoryCode),
                        new XElement(cbc + "Percent", request.Lines.First().TaxRate.ToString("0.00", CultureInfo.InvariantCulture)),
                        new XElement(
                            cac + "TaxScheme",
                            new XElement(cbc + "ID", "VAT")
                        )
                    )
                )
            ),

            new XElement(
                cac + "LegalMonetaryTotal",
                new XElement(
                    cbc + "LineExtensionAmount",
                    new XAttribute("currencyID", currencyCode),
                    FormatAmount(totals.LineExtensionAmount)
                ),
                new XElement(
                    cbc + "TaxExclusiveAmount",
                    new XAttribute("currencyID", currencyCode),
                    FormatAmount(totals.TaxExclusiveAmount)
                ),
                new XElement(
                    cbc + "TaxInclusiveAmount",
                    new XAttribute("currencyID", currencyCode),
                    FormatAmount(totals.TaxInclusiveAmount)
                ),
                new XElement(
                    cbc + "AllowanceTotalAmount",
                    new XAttribute("currencyID", currencyCode),
                    FormatAmount(totals.AllowanceTotalAmount)
                ),
                new XElement(
                    cbc + "PayableAmount",
                    new XAttribute("currencyID", currencyCode),
                    FormatAmount(totals.TaxInclusiveAmount)
                )
            ),

            request.Lines.Select((line, index) =>
                new XElement(
                    cac + "InvoiceLine",
                    new XElement(cbc + "ID", (index + 1).ToString(CultureInfo.InvariantCulture)),
                    new XElement(
                        cbc + "InvoicedQuantity",
                        new XAttribute("unitCode", line.UnitCode),
                        line.Quantity.ToString("0.##", CultureInfo.InvariantCulture)
                    ),
                    new XElement(
                        cbc + "LineExtensionAmount",
                        new XAttribute("currencyID", currencyCode),
                        FormatAmount(GetLineNetAmount(line))
                    ),
                    new XElement(
                        cac + "TaxTotal",
                        new XElement(
                            cbc + "TaxAmount",
                            new XAttribute("currencyID", taxCurrencyCode),
                            FormatAmount(GetLineTaxAmount(line))
                        ),
                        new XElement(
                            cbc + "RoundingAmount",
                            new XAttribute("currencyID", currencyCode),
                            FormatAmount(GetLineGrossAmount(line))
                        )
                    ),
                    line.DiscountAmount <= 0m
                        ? null
                        : new XElement(
                            cac + "AllowanceCharge",
                            new XElement(cbc + "ChargeIndicator", false),
                            new XElement(
                                cbc + "Amount",
                                new XAttribute("currencyID", currencyCode),
                                FormatAmount(line.DiscountAmount)
                            )
                        ),
                    new XElement(
                        cac + "Item",
                        new XElement(cbc + "Name", line.Name),
                        new XElement(
                            cac + "ClassifiedTaxCategory",
                            new XElement(cbc + "ID", line.TaxCategoryCode),
                            new XElement(cbc + "Percent", line.TaxRate.ToString("0.00", CultureInfo.InvariantCulture)),
                            new XElement(
                                cac + "TaxScheme",
                                new XElement(cbc + "ID", "VAT")
                            )
                        )
                    ),
                    new XElement(
                        cac + "Price",
                        new XElement(
                            cbc + "PriceAmount",
                            new XAttribute("currencyID", currencyCode),
                            FormatAmount(line.UnitPrice)
                        )
                    )
                )
            )
        );

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            invoice
        );

        var builder = new StringBuilder();
        using var writer = new System.IO.StringWriter(builder);
        document.Save(writer, SaveOptions.DisableFormatting);
        return builder.ToString();
    }

    private static XElement BuildAdditionalDocumentReference(XNamespace cac, XNamespace cbc, string id, string? uuid, string? embeddedValue)
    {
        return new XElement(cac + "AdditionalDocumentReference",
            new XElement(cbc + "ID", id),
            string.IsNullOrWhiteSpace(uuid) ? null : new XElement(cbc + "UUID", uuid),
            new XElement(cac + "Attachment",
                new XElement(cbc + "EmbeddedDocumentBinaryObject",
                    new XAttribute("mimeCode", "text/plain"),
                    embeddedValue ?? string.Empty)));
    }

    private static XElement BuildPartyElement(XNamespace cac, XNamespace cbc, PartyInfo party)
    {
        return new XElement(cac + "Party",
            new XElement(cac + "PostalAddress",
                string.IsNullOrWhiteSpace(party.StreetName) ? null : new XElement(cbc + "StreetName", party.StreetName),
                string.IsNullOrWhiteSpace(party.BuildingNumber) ? null : new XElement(cbc + "BuildingNumber", party.BuildingNumber),
                string.IsNullOrWhiteSpace(party.PlotIdentification) ? null : new XElement(cbc + "PlotIdentification", party.PlotIdentification),
                string.IsNullOrWhiteSpace(party.CitySubdivisionName) ? null : new XElement(cbc + "CitySubdivisionName", party.CitySubdivisionName),
                string.IsNullOrWhiteSpace(party.CityName) ? null : new XElement(cbc + "CityName", party.CityName),
                string.IsNullOrWhiteSpace(party.PostalZone) ? null : new XElement(cbc + "PostalZone", party.PostalZone),
                new XElement(cac + "Country", new XElement(cbc + "IdentificationCode", party.CountryCode))),
            new XElement(cac + "PartyTaxScheme",
                new XElement(cbc + "CompanyID", party.VatNumber),
                new XElement(cac + "TaxScheme", new XElement(cbc + "ID", "VAT"))),
            new XElement(cac + "PartyLegalEntity",
                new XElement(cbc + "RegistrationName", party.RegistrationName),
                string.IsNullOrWhiteSpace(party.RegistrationNumber) ? null : new XElement(cbc + "CompanyID", party.RegistrationNumber)));
    }

    private static (decimal LineExtensionAmount, decimal TaxExclusiveAmount, decimal TaxInclusiveAmount, decimal TotalTaxAmount, decimal AllowanceTotalAmount) CalculateTotals(IEnumerable<InvoiceLineItem> lines)
    {
        var lineList = lines.ToList();
        var lineExtensionAmount = lineList.Sum(GetLineNetAmount);
        var totalTaxAmount = lineList.Sum(GetLineTaxAmount);
        var allowanceTotalAmount = lineList.Sum(x => Round(x.DiscountAmount));
        var taxExclusiveAmount = Round(lineExtensionAmount);
        var taxInclusiveAmount = Round(taxExclusiveAmount + totalTaxAmount);

        return (Round(lineExtensionAmount), taxExclusiveAmount, taxInclusiveAmount, Round(totalTaxAmount), allowanceTotalAmount);
    }

    private static decimal GetLineNetAmount(InvoiceLineItem line)
    {
        var gross = line.Quantity * line.UnitPrice;
        return Round(gross - line.DiscountAmount);
    }

    private static decimal GetLineTaxAmount(InvoiceLineItem line)
    {
        return Round(GetLineNetAmount(line) * (line.TaxRate / 100m));
    }

    private static decimal GetLineGrossAmount(InvoiceLineItem line)
    {
        return Round(GetLineNetAmount(line) + GetLineTaxAmount(line));
    }

    private static decimal Round(decimal value)
        => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string FormatAmount(decimal value)
        => Round(value).ToString("0.00", CultureInfo.InvariantCulture);

    private static string? ResolveCertificateToken(DeviceState state)
        => state.ProductionBinarySecurityToken ?? state.ComplianceBinarySecurityToken;
}

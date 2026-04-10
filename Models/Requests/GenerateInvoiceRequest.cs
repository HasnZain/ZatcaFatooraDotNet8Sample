using System.ComponentModel.DataAnnotations;
using ZatcaFatooraDotNet8Sample.Models.Domain;

namespace ZatcaFatooraDotNet8Sample.Models.Requests;

public sealed class GenerateInvoiceRequest : IValidatableObject
{
    [Required]
    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary>
    /// "Standard" or "Simplified"
    /// </summary>
    [Required]
    public string InvoiceCategory { get; set; } = "Standard";

    /// <summary>
    /// UBL invoice type code:
    ///   388 = Tax Invoice (Standard or Simplified)
    ///   381 = Credit Note
    ///   383 = Debit Note
    /// </summary>
    public string InvoiceTypeCode { get; set; } = "388";

    /// <summary>
    /// 7-digit binary string for the "name" attribute on InvoiceTypeCode.
    /// If omitted the service derives the correct value from InvoiceCategory and InvoiceTypeCode.
    /// </summary>
    public string? InvoiceTypeName { get; set; }

    public DateTimeOffset? IssueDateTime { get; set; }
    public string CurrencyCode { get; set; } = "SAR";
    public string TaxCurrencyCode { get; set; } = "SAR";

    /// <summary>
    /// Payment means code per UN/CEFACT list.
    ///   10 = Cash, 30 = Credit transfer, 42 = Bank account, 48 = Bank card, etc.
    /// </summary>
    public string PaymentMeansCode { get; set; } = "10";

    public string? Notes { get; set; }

    /// <summary>Optional <c>languageID</c> on UBL <c>cbc:Note</c> (ZATCA SDK sample uses <c>ar</c>).</summary>
    public string? NoteLanguageId { get; set; }
    public string? PreviousInvoiceHashBase64 { get; set; }
    public long? InvoiceCounterValue { get; set; }

    /// <summary>
    /// Required for credit notes (381) and debit notes (383).
    /// Must reference the original invoice number.
    /// </summary>
    public string? BillingReferenceId { get; set; }

    /// <summary>
    /// Required for credit notes and debit notes.
    /// </summary>
    public string? CreditDebitReason { get; set; }

    [Required]
    public PartyInfo Seller { get; set; } = new();

    /// <summary>
    /// Required for Standard invoices and Standard credit/debit notes.
    /// Optional for Simplified invoices (a walk-in placeholder is used if omitted).
    /// </summary>
    public PartyInfo? Buyer { get; set; }

    [MinLength(1)]
    public List<InvoiceLineItem> Lines { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Validate InvoiceCategory
        if (!string.Equals(InvoiceCategory, "Standard", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(InvoiceCategory, "Simplified", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ValidationResult(
                "InvoiceCategory must be 'Standard' or 'Simplified'.",
                new[] { nameof(InvoiceCategory) });
        }

        // Validate InvoiceTypeCode
        if (InvoiceTypeCode != "388" && InvoiceTypeCode != "381" && InvoiceTypeCode != "383")
        {
            yield return new ValidationResult(
                "InvoiceTypeCode must be 388 (invoice), 381 (credit note), or 383 (debit note).",
                new[] { nameof(InvoiceTypeCode) });
        }

        var isCreditOrDebitNote = InvoiceTypeCode == "381" || InvoiceTypeCode == "383";
        var isStandard = string.Equals(InvoiceCategory, "Standard", StringComparison.OrdinalIgnoreCase);

        // Standard invoices always require a buyer
        if (isStandard && Buyer is null)
        {
            yield return new ValidationResult(
                "Buyer is required for Standard invoices and Standard credit/debit notes.",
                new[] { nameof(Buyer) });
        }

        // Credit and debit notes require BillingReferenceId
        if (isCreditOrDebitNote && string.IsNullOrWhiteSpace(BillingReferenceId))
        {
            yield return new ValidationResult(
                "BillingReferenceId is required for credit notes (381) and debit notes (383).",
                new[] { nameof(BillingReferenceId) });
        }

        // Credit and debit notes require a reason
        if (isCreditOrDebitNote && string.IsNullOrWhiteSpace(CreditDebitReason))
        {
            yield return new ValidationResult(
                "CreditDebitReason is required for credit notes (381) and debit notes (383).",
                new[] { nameof(CreditDebitReason) });
        }

        // Must have at least one line item
        if (Lines == null || Lines.Count == 0)
        {
            yield return new ValidationResult(
                "At least one invoice line is required.",
                new[] { nameof(Lines) });
        }

        // Validate InvoiceTypeName format if provided
        if (!string.IsNullOrWhiteSpace(InvoiceTypeName) &&
            (InvoiceTypeName.Length != 7 || !InvoiceTypeName.All(c => c == '0' || c == '1' || c == '2')))
        {
            yield return new ValidationResult(
                "InvoiceTypeName must be a 7-character string of digits (e.g. 0100000, 0200000, 0110000).",
                new[] { nameof(InvoiceTypeName) });
        }
    }
}
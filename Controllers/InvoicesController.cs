using Microsoft.AspNetCore.Mvc;
using ZatcaFatooraDotNet8Sample.Models.Requests;
using ZatcaFatooraDotNet8Sample.Models.Responses;
using ZatcaFatooraDotNet8Sample.Services;

namespace ZatcaFatooraDotNet8Sample.Controllers;

[ApiController]
[Route("api/invoices")]
public sealed class InvoicesController : ControllerBase
{
    private readonly DeviceStateStoreService _deviceStateStoreService;
    private readonly InvoiceXmlBuilderService _invoiceXmlBuilderService;
    private readonly ZatcaApiClient _zatcaApiClient;

    public InvoicesController(
        DeviceStateStoreService deviceStateStoreService,
        InvoiceXmlBuilderService invoiceXmlBuilderService,
        ZatcaApiClient zatcaApiClient)
    {
        _deviceStateStoreService = deviceStateStoreService;
        _invoiceXmlBuilderService = invoiceXmlBuilderService;
        _zatcaApiClient = zatcaApiClient;
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateInvoiceRequest request, CancellationToken cancellationToken)
    {
        var state = await _deviceStateStoreService.LoadAsync(cancellationToken);
        var result = _invoiceXmlBuilderService.Build(request, state);

        state.LastInvoiceCounterValue = result.InvoiceCounterValue;
        state.LastInvoiceHashBase64 = result.InvoiceHashBase64;
        await _deviceStateStoreService.SaveAsync(state, cancellationToken);

        var response = new InvoiceGenerationResponse
        {
            InvoiceNumber = result.InvoiceNumber,
            Uuid = result.Uuid,
            InvoiceCounterValue = result.InvoiceCounterValue,
            PreviousInvoiceHashBase64 = result.PreviousInvoiceHashBase64,
            InvoiceHash = result.InvoiceHashBase64,
            SignatureBase64 = result.SignatureBase64,
            QrCodeBase64 = result.QrCodeBase64,
            SignedXml = result.SignedXml,
            InvoiceBase64 = result.InvoiceBase64,
            TaxExclusiveAmount = result.TaxExclusiveAmount,
            TaxInclusiveAmount = result.TaxInclusiveAmount,
            TotalTaxAmount = result.TotalTaxAmount
        };

        return Ok(response);
    }

    [HttpPost("report-simplified")]
    public async Task<IActionResult> ReportSimplified([FromBody] SubmitInvoiceRequest request, CancellationToken cancellationToken)
    {
        var state = await _deviceStateStoreService.LoadAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(state.ProductionBinarySecurityToken) || string.IsNullOrWhiteSpace(state.ProductionSecret))
        {
            return BadRequest("Production CSID is missing. Call api/onboarding/production-csid first.");
        }

        var response = await _zatcaApiClient.ReportInvoiceAsync(
            state.ProductionBinarySecurityToken,
            state.ProductionSecret,
            request.InvoiceHash,
            request.Uuid,
            request.InvoiceBase64,
            cancellationToken);

        return StatusCode(response.StatusCode, response);
    }

    [HttpPost("clear-standard")]
    public async Task<IActionResult> ClearStandard([FromBody] SubmitInvoiceRequest request, CancellationToken cancellationToken)
    {
        var state = await _deviceStateStoreService.LoadAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(state.ProductionBinarySecurityToken) || string.IsNullOrWhiteSpace(state.ProductionSecret))
        {
            return BadRequest("Production CSID is missing. Call api/onboarding/production-csid first.");
        }

        var response = await _zatcaApiClient.ClearInvoiceAsync(
            state.ProductionBinarySecurityToken,
            state.ProductionSecret,
            request.InvoiceHash,
            request.Uuid,
            request.InvoiceBase64,
            cancellationToken);

        return StatusCode(response.StatusCode, response);
    }
}

using Microsoft.AspNetCore.Mvc;
using ZatcaFatooraDotNet8Sample.Models.Requests;
using ZatcaFatooraDotNet8Sample.Services;

namespace ZatcaFatooraDotNet8Sample.Controllers;

[ApiController]
[Route("api/onboarding")]
public sealed class OnboardingController : ControllerBase
{
    private readonly DeviceStateStoreService _deviceStateStoreService;
    private readonly ZatcaApiClient _zatcaApiClient;

    public OnboardingController(DeviceStateStoreService deviceStateStoreService, ZatcaApiClient zatcaApiClient)
    {
        _deviceStateStoreService = deviceStateStoreService;
        _zatcaApiClient = zatcaApiClient;
    }

    [HttpPost("compliance-csid")]
    public async Task<IActionResult> CreateComplianceCsid([FromBody] ComplianceCsidRequest request, CancellationToken cancellationToken)
    {
        var state = await _deviceStateStoreService.LoadAsync(cancellationToken);
        var csrBase64 = request.CsrBase64 ?? state.CsrBase64;

        if (string.IsNullOrWhiteSpace(csrBase64))
        {
            return BadRequest("No CSR found. Generate CSR first or pass csrBase64 in the request body.");
        }

        var (response, raw) = await _zatcaApiClient.CreateComplianceCsidAsync(csrBase64, request.Otp, cancellationToken);

        if (response is not null)
        {
            state.ComplianceRequestId = response.RequestId.ToString();
            state.ComplianceBinarySecurityToken = response.BinarySecurityToken;
            state.ComplianceSecret = response.Secret;
            await _deviceStateStoreService.SaveAsync(state, cancellationToken);
        }

        return StatusCode(raw.StatusCode, new
        {
            raw.StatusCode,
            raw.Content,
            parsed = response
        });
    }

    [HttpPost("compliance-check")]
    public async Task<IActionResult> CheckComplianceInvoice([FromBody] ComplianceInvoiceRequest request, CancellationToken cancellationToken)
    {
        var state = await _deviceStateStoreService.LoadAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(state.ComplianceBinarySecurityToken) || string.IsNullOrWhiteSpace(state.ComplianceSecret))
        {
            return BadRequest("Compliance CSID is missing. Call api/onboarding/compliance-csid first.");
        }

        var response = await _zatcaApiClient.CheckComplianceInvoiceAsync(
            state.ComplianceBinarySecurityToken,
            state.ComplianceSecret,
            request.InvoiceHash,
            request.Uuid,
            request.InvoiceBase64,
            cancellationToken);

        return StatusCode(response.StatusCode, response);
    }

    [HttpPost("production-csid")]
    public async Task<IActionResult> CreateProductionCsid([FromBody] ProductionCsidRequest request, CancellationToken cancellationToken)
    {
        var state = await _deviceStateStoreService.LoadAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(state.ComplianceBinarySecurityToken) || string.IsNullOrWhiteSpace(state.ComplianceSecret))
        {
            return BadRequest("Compliance CSID is missing. Call api/onboarding/compliance-csid first.");
        }

        var complianceRequestId = request.ComplianceRequestId ?? state.ComplianceRequestId;
        if (string.IsNullOrWhiteSpace(complianceRequestId))
        {
            return BadRequest("Compliance request ID is missing.");
        }

        var (response, raw) = await _zatcaApiClient.CreateProductionCsidAsync(
            complianceRequestId,
            state.ComplianceBinarySecurityToken,
            state.ComplianceSecret,
            cancellationToken);

        if (response is not null)
        {
            state.ProductionBinarySecurityToken = response.BinarySecurityToken;
            state.ProductionSecret = response.Secret;
            await _deviceStateStoreService.SaveAsync(state, cancellationToken);
        }

        return StatusCode(raw.StatusCode, new
        {
            raw.StatusCode,
            raw.Content,
            parsed = response
        });
    }
}

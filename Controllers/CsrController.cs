using Microsoft.AspNetCore.Mvc;
using ZatcaFatooraDotNet8Sample.Models.Requests;
using ZatcaFatooraDotNet8Sample.Services;

namespace ZatcaFatooraDotNet8Sample.Controllers;

[ApiController]
[Route("api/csr")]
public sealed class CsrController : ControllerBase
{
    private readonly CsrGeneratorService _csrGeneratorService;
    private readonly DeviceStateStoreService _deviceStateStoreService;

    public CsrController(
        CsrGeneratorService csrGeneratorService,
        DeviceStateStoreService deviceStateStoreService)
    {
        _csrGeneratorService = csrGeneratorService;
        _deviceStateStoreService = deviceStateStoreService;
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate(
        [FromBody] GenerateCsrRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var csr = _csrGeneratorService.Generate(request);

        var state = await _deviceStateStoreService.LoadAsync(cancellationToken);
        state.SubjectName = csr.SubjectDebug;
        state.PrivateKeyPem = csr.PrivateKeyPem;
        state.PublicKeyPem = csr.PublicKeyPem;
        state.CsrPem = csr.CsrPem;
        state.CsrBase64 = csr.CsrBase64;

        await _deviceStateStoreService.SaveAsync(state, cancellationToken);

        return Ok(csr);
    }

    [HttpGet("state")]
    public async Task<IActionResult> GetState(CancellationToken cancellationToken)
    {
        var state = await _deviceStateStoreService.LoadAsync(cancellationToken);
        return Ok(state);
    }
}
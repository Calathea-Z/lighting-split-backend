// Api/Controllers/ShareApiController.cs

using Api.Dtos.Splits.Responses;
using Api.Services.Payments.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/s")]
public sealed class ShareApiController(ISplitShareReader shareSplitReader) : ControllerBase
{
    // GET /api/s/{code}
    [HttpGet("{code}")]
    public async Task<ActionResult<ShareSplitResponseDto>> Get(string code)
    {
        try
        {
            var dto = await shareSplitReader.GetByCodeAsync(code);
            return Ok(dto);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}

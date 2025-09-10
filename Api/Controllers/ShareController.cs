// Api/Controllers/ShareController.cs

using Api.Dtos.Splits.Responses;
using Api.Services.Payments.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("s")]
public sealed class ShareController(ISplitShareReader shareSplitReader) : ControllerBase
{
    // GET /s/{code}
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

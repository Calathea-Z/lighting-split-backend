using Api.Data;
using Api.Infrastructure.Middleware;
using Api.Services.Events;
using Api.Services.Events.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Api.Controllers;

[ApiController]
[Route("api/events")]
public sealed class EventsController : ControllerBase
{
    private readonly IEventStreamService _eventStream;
    private readonly LightningDbContext _db;
    private readonly ILogger<EventsController> _logger;

    public EventsController(
        IEventStreamService eventStream,
        LightningDbContext db,
        ILogger<EventsController> logger)
    {
        _eventStream = eventStream;
        _db = db;
        _logger = logger;
    }

    // GET /api/events/public/{shareCode}
    [HttpGet("public/{shareCode}")]
    public async Task<IActionResult> GetPublicEvents(string shareCode)
    {
        // Verify share code exists and is active
        var split = await _db.SplitSessions
            .FirstOrDefaultAsync(s => s.ShareCode == shareCode && s.IsFinalized);

        if (split is null)
        {
            return NotFound("Share code not found or split not finalized");
        }

        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";
        Response.Headers["Access-Control-Allow-Origin"] = "*";
        Response.Headers["Access-Control-Allow-Headers"] = "Cache-Control";

        var writer = new StreamWriter(Response.Body, Encoding.UTF8);

        // Send initial connection event
        await writer.WriteAsync("data: {\"type\":\"connected\",\"shareCode\":\"" + shareCode + "\"}\n\n");
        await writer.FlushAsync();

        // Add to event stream service
        _eventStream.AddPublicStream(shareCode, writer);

        // Keep connection alive with periodic pings
        var cancellationToken = HttpContext.RequestAborted;
        var pingInterval = TimeSpan.FromSeconds(25);
        var lastPing = DateTimeOffset.UtcNow;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);

                // Send ping if interval has passed
                if (DateTimeOffset.UtcNow - lastPing >= pingInterval)
                {
                    await writer.WriteAsync("data: {\"type\":\"ping\"}\n\n");
                    await writer.FlushAsync();
                    lastPing = DateTimeOffset.UtcNow;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
        }
        finally
        {
            _eventStream.RemoveStream(shareCode, writer);
            await writer.DisposeAsync();
        }

        return new EmptyResult();
    }

    // GET /api/events/owner/{splitId}
    [HttpGet("owner/{splitId:guid}")]
    public async Task<IActionResult> GetOwnerEvents(Guid splitId)
    {
        var owner = HttpContext.GetOwner();

        // Verify split ownership
        var split = await _db.SplitSessions
            .FirstOrDefaultAsync(s => s.Id == splitId && s.OwnerId == owner.Id);

        if (split is null)
        {
            return NotFound("Split not found or access denied");
        }

        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";
        Response.Headers["Access-Control-Allow-Origin"] = "*";
        Response.Headers["Access-Control-Allow-Headers"] = "Cache-Control";

        var writer = new StreamWriter(Response.Body, Encoding.UTF8);

        // Send initial connection event
        await writer.WriteAsync("data: {\"type\":\"connected\",\"splitId\":\"" + splitId + "\"}\n\n");
        await writer.FlushAsync();

        // Add to event stream service
        _eventStream.AddOwnerStream(splitId.ToString(), writer);

        // Keep connection alive with periodic pings
        var cancellationToken = HttpContext.RequestAborted;
        var pingInterval = TimeSpan.FromSeconds(25);
        var lastPing = DateTimeOffset.UtcNow;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);

                // Send ping if interval has passed
                if (DateTimeOffset.UtcNow - lastPing >= pingInterval)
                {
                    await writer.WriteAsync("data: {\"type\":\"ping\"}\n\n");
                    await writer.FlushAsync();
                    lastPing = DateTimeOffset.UtcNow;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected
        }
        finally
        {
            _eventStream.RemoveOwnerStream(splitId.ToString(), writer);
            await writer.DisposeAsync();
        }

        return new EmptyResult();
    }
}

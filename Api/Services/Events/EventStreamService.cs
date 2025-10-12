using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Api.Models.Events;
using Api.Services.Events.Abstractions;

namespace Api.Services.Events;

public sealed class EventStreamService : IEventStreamService
{
    private readonly ConcurrentDictionary<string, List<StreamWriter>> _publicStreams = new();
    private readonly ConcurrentDictionary<string, List<StreamWriter>> _ownerStreams = new();
    private readonly ILogger<EventStreamService> _logger;
    private long _version = 0;

    public EventStreamService(ILogger<EventStreamService> logger)
    {
        _logger = logger;
    }

    public async Task EmitPaymentStatusChangedAsync(PaymentStatusChangedEvent evt)
    {
        var version = Interlocked.Increment(ref _version);
        evt.Version = version;

        var json = JsonSerializer.Serialize(evt);
        var data = $"data: {json}\n\n";

        // Emit to public streams (by share code)
        await EmitToStreamsAsync(_publicStreams, data, "PaymentStatusChanged");

        // Emit to owner streams (by split ID)
        await EmitToStreamsAsync(_ownerStreams, data, "PaymentStatusChanged");
    }

    public async Task EmitShareRevokedAsync(ShareRevokedEvent evt)
    {
        var json = JsonSerializer.Serialize(evt);
        var data = $"data: {json}\n\n";

        // Emit to public streams for this share code
        await EmitToStreamsAsync(_publicStreams, data, "ShareRevoked", evt.ShareCode);
    }

    public async Task EmitSplitRefinalizedAsync(SplitRefinalizedEvent evt)
    {
        var json = JsonSerializer.Serialize(evt);
        var data = $"data: {json}\n\n";

        // Emit to owner streams for this split
        await EmitToStreamsAsync(_ownerStreams, data, "SplitRefinalized", evt.SplitId.ToString());
    }

    public void AddPublicStream(string shareCode, StreamWriter writer)
    {
        _publicStreams.AddOrUpdate(
            shareCode,
            new List<StreamWriter> { writer },
            (key, existing) =>
            {
                lock (existing)
                {
                    existing.Add(writer);
                }
                return existing;
            });

        _logger.LogInformation("Added public stream for share code: {ShareCode}", shareCode);
    }

    public void AddOwnerStream(string splitId, StreamWriter writer)
    {
        _ownerStreams.AddOrUpdate(
            splitId,
            new List<StreamWriter> { writer },
            (key, existing) =>
            {
                lock (existing)
                {
                    existing.Add(writer);
                }
                return existing;
            });

        _logger.LogInformation("Added owner stream for split: {SplitId}", splitId);
    }

    public void RemoveStream(string shareCode, StreamWriter writer)
    {
        if (_publicStreams.TryGetValue(shareCode, out var publicStreams))
        {
            lock (publicStreams)
            {
                publicStreams.Remove(writer);
                if (publicStreams.Count == 0)
                {
                    _publicStreams.TryRemove(shareCode, out _);
                }
            }
        }
    }

    public void RemoveOwnerStream(string splitId, StreamWriter writer)
    {
        if (_ownerStreams.TryGetValue(splitId, out var ownerStreams))
        {
            lock (ownerStreams)
            {
                ownerStreams.Remove(writer);
                if (ownerStreams.Count == 0)
                {
                    _ownerStreams.TryRemove(splitId, out _);
                }
            }
        }
    }

    private async Task EmitToStreamsAsync(
        ConcurrentDictionary<string, List<StreamWriter>> streams,
        string data,
        string eventType,
        string? specificKey = null)
    {
        var tasks = new List<Task>();

        foreach (var kvp in streams)
        {
            // If specificKey is provided, only emit to that key
            if (specificKey != null && kvp.Key != specificKey)
                continue;

            lock (kvp.Value)
            {
                var deadStreams = new List<StreamWriter>();

                foreach (var writer in kvp.Value)
                {
                    try
                    {
                        var task = writer.WriteAsync(data);
                        tasks.Add(task);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to write to stream for {Key}", kvp.Key);
                        deadStreams.Add(writer);
                    }
                }

                // Remove dead streams
                foreach (var dead in deadStreams)
                {
                    kvp.Value.Remove(dead);
                }

                // Clean up empty lists
                if (kvp.Value.Count == 0)
                {
                    streams.TryRemove(kvp.Key, out _);
                }
            }
        }

        await Task.WhenAll(tasks);
    }
}

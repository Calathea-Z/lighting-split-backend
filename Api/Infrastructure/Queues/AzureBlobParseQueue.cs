using System.Text.Json;
using Api.Contracts.Receipts;
using Api.Infrastructure.Interfaces;
using Azure.Storage.Queues;

namespace Api.Infrastructure.Queues;

public sealed class AzureBlobParseQueue(QueueServiceClient svc) : IParseQueue
{
    private readonly QueueClient _q = svc.GetQueueClient("receipt-parse");

    public async Task EnqueueAsync(ReceiptParseMessage msg, CancellationToken ct = default)
    {
        await _q.CreateIfNotExistsAsync(cancellationToken: ct);

        var json = JsonSerializer.Serialize(msg, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // Optional: structured logging instead of Console.WriteLine
        Console.WriteLine($"[ParseQueue] Enqueuing receipt {msg.ReceiptId} -> {json}");

        // SDK auto-encodes to Base64, so just send the JSON
        await _q.SendMessageAsync(json, cancellationToken: ct);
    }
}

using System.Text;
using System.Text.Json;
using Api.Contracts;
using Api.Interfaces;
using Azure.Storage.Queues;

namespace Api.Services;


public sealed class AzureStorageParseQueue(QueueServiceClient svc) : IParseQueue
{
    private readonly QueueClient _q = svc.GetQueueClient("receipt-parse");
    public async Task EnqueueAsync(ReceiptParseMessage msg, CancellationToken ct = default)
    {
        await _q.CreateIfNotExistsAsync(cancellationToken: ct);
        var json = JsonSerializer.Serialize(msg);
        
        // Debug logging
        Console.WriteLine($"Enqueuing message: {json}");
        
        // Send the JSON directly - Azure Storage Queues handle encoding automatically
        await _q.SendMessageAsync(json, cancellationToken: ct);
    }
}

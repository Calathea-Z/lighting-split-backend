using Api.Abstractions.Transport;

namespace Api.Infrastructure.Interfaces;

public interface IParseQueue { Task EnqueueAsync(ReceiptParseMessage msg, CancellationToken ct = default); }
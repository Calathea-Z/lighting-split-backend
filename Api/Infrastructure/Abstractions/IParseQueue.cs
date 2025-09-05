using Api.Contracts.Receipts;

namespace Api.Infrastructure.Interfaces;

public interface IParseQueue { Task EnqueueAsync(ReceiptParseMessage msg, CancellationToken ct = default); }
using Api.Contracts;

namespace Api.Interfaces;

public interface IParseQueue { Task EnqueueAsync(ReceiptParseMessage msg, CancellationToken ct = default); }
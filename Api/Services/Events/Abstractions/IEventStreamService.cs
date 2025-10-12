using Api.Models.Events;
using System.IO;

namespace Api.Services.Events.Abstractions;

public interface IEventStreamService
{
    Task EmitPaymentStatusChangedAsync(PaymentStatusChangedEvent evt);
    Task EmitShareRevokedAsync(ShareRevokedEvent evt);
    Task EmitSplitRefinalizedAsync(SplitRefinalizedEvent evt);
    void AddPublicStream(string shareCode, StreamWriter writer);
    void AddOwnerStream(string splitId, StreamWriter writer);
    void RemoveStream(string shareCode, StreamWriter writer);
    void RemoveOwnerStream(string splitId, StreamWriter writer);
}

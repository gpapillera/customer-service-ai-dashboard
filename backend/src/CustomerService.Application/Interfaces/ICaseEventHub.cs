using CustomerService.Application.Dtos;

namespace CustomerService.Application.Interfaces;

/// <summary>
/// In-process publish/subscribe hub for case assignment changes. Implemented as
/// a singleton so a single <c>System.Threading.Channels.Channel&lt;CaseEvent&gt;</c>
/// backs every SSE subscriber; the controller reads from <see cref="Reader"/>
/// and the service layer writes via <see cref="PublishAsync"/>. No external
/// infrastructure (Redis/RabbitMQ) needed for a single-instance demo — see the
/// <c>ponytail:</c> note on <see cref="CaseEventHub"/> for the scale-up path.
/// </summary>
public interface ICaseEventHub
{
    /// <summary>Enqueues an assignment change for broadcast to all subscribers.</summary>
    ValueTask PublishAsync(CaseEvent evt);

    /// <summary>Stream of events for an SSE subscriber to enumerate.</summary>
    System.Threading.Channels.ChannelReader<CaseEvent> Reader { get; }
}

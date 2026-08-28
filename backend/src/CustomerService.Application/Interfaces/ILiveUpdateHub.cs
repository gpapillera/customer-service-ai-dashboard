using CustomerService.Application.Dtos;

namespace CustomerService.Application.Interfaces;

/// <summary>
/// In-process publish/subscribe hub for ALL real-time mutations (case + customer
/// + account events). Implemented as a singleton. Each SSE subscriber calls
/// <see cref="Subscribe"/> to get its own dedicated event reader, and the service
/// layer broadcasts via <see cref="PublishAsync"/> to every subscriber (true
/// fan-out, not queue semantics). No external infrastructure (Redis/RabbitMQ)
/// needed for a single-instance demo — see the <c>ponytail:</c> note on
/// <see cref="LiveUpdateHub"/> for the scale-up path.
/// </summary>
public interface ILiveUpdateHub
{
    /// <summary>Enqueues a mutation event for broadcast to all subscribers.</summary>
    ValueTask PublishAsync(LiveUpdateEvent evt);

    /// <summary>Registers a new SSE subscriber and returns its dedicated event reader.</summary>
    System.Threading.Channels.ChannelReader<LiveUpdateEvent> Subscribe();
}

using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace CustomerService.Application.Services;

/// <summary>
/// Singleton in-memory hub for all real-time mutation events (cases, customers,
/// accounts). IMPORTANT: a single <see cref="Channel{T}"/> is a QUEUE, not a
/// broadcast — each published item is consumed by exactly ONE reader. SSE needs
/// true fan-out (every connected client gets every event), so this hub gives each
/// subscriber its OWN unbounded channel and <see cref="PublishAsync"/> writes the
/// event to ALL of them. Dead subscribers (their SSE client disconnected) are
/// pruned lazily on the next publish.
///
/// Unbounded channels are acceptable for a demo (event volume is low and bounded
/// by human actions). If this ever moves behind a load balancer or sees high
/// throughput, swap the in-process fan-out for a distributed bus (Redis pub/sub,
/// Azure SignalR, RabbitMQ) and keep this interface — that is the only call-site
/// that would change (<c>ponytail:</c> scale-up path).
/// </summary>
public sealed class LiveUpdateHub : ILiveUpdateHub
{
    // Each connected SSE client gets its own channel; PublishAsync fans out to all.
    private readonly ConcurrentDictionary<Channel<LiveUpdateEvent>, byte> _subscribers = new();

    /// <summary>
    /// Registers a new subscriber and returns the reader it should enumerate.
    /// Safe to call concurrently; the returned channel is dedicated to one SSE stream.
    /// </summary>
    public ChannelReader<LiveUpdateEvent> Subscribe()
    {
        var ch = Channel.CreateUnbounded<LiveUpdateEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        _subscribers[ch] = 0;
        return ch.Reader;
    }

    /// <summary>
    /// Publishes <paramref name="evt"/> to EVERY subscriber (true broadcast).
    /// A write that fails (subscriber disconnected) prunes that subscriber.
    /// </summary>
    public ValueTask PublishAsync(LiveUpdateEvent evt)
    {
        foreach (var sub in _subscribers.Keys)
        {
            // Best-effort: a disconnected client's channel write throws — drop it.
            if (!sub.Writer.TryWrite(evt))
            {
                _subscribers.TryRemove(sub, out _);
                sub.Writer.Complete();
            }
        }
        return default;
    }
}

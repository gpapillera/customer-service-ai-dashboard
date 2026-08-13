using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using System.Threading.Channels;

namespace CustomerService.Application.Services;

/// <summary>
/// Singleton in-memory hub for case assignment events. A single unbounded
/// channel fans out every <see cref="PublishAsync"/> to all connected SSE
/// readers. Unbounded is acceptable for a demo (event volume is low and bounded
/// by human assignment actions); if this ever moves behind a load balancer or
/// sees high throughput, swap the channel for a distributed bus (Redis pub/sub,
/// Azure SignalR, RabbitMQ) and keep this interface — that is the only call-site
/// that would change.
/// </summary>
public sealed class CaseEventHub : ICaseEventHub
{
    private readonly Channel<CaseEvent> _channel = Channel.CreateUnbounded<CaseEvent>(
        new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    public ChannelReader<CaseEvent> Reader => _channel.Reader;

    public ValueTask PublishAsync(CaseEvent evt) => _channel.Writer.WriteAsync(evt);
}

using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using System.Threading.Channels;

namespace CustomerService.Tests.Fakes;

/// <summary>
/// In-memory stand-in for <see cref="ILiveUpdateHub"/> so unit tests can
/// construct <c>CaseService</c> / <c>CustomerService</c> / <c>CustomerAuthService</c>
/// without the production singleton. Collects every published
/// <see cref="LiveUpdateEvent"/> for assertions. Mirrors the deleted
/// <c>FakeCaseEventHub</c> / <c>FakeCustomerEventHub</c>.
/// </summary>
public sealed class FakeLiveUpdateHub : ILiveUpdateHub
{
    private readonly Channel<LiveUpdateEvent> _channel =
        Channel.CreateUnbounded<LiveUpdateEvent>();

    public ChannelReader<LiveUpdateEvent> Subscribe() => _channel.Reader;

    public ValueTask PublishAsync(LiveUpdateEvent evt) => _channel.Writer.WriteAsync(evt);

    /// <summary>Non-blocking read of the next published event (for assertions).</summary>
    public bool TryRead(out LiveUpdateEvent evt) => _channel.Reader.TryRead(out evt!);

    /// <summary>Drains published events for assertions (non-blocking).</summary>
    public List<LiveUpdateEvent> Published()
    {
        var out_ = new List<LiveUpdateEvent>();
        while (_channel.Reader.TryRead(out var evt))
        {
            out_.Add(evt);
        }
        return out_;
    }
}

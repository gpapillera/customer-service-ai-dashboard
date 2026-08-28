using System.Text;
using System.Text.Json;
using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerService.Api.Controllers;

/// <summary>
/// Server-Sent Events (SSE) stream of every staff (and customer self-service)
/// mutation. The frontend connects with the staff JWT (sent as a Bearer header
/// via a streaming <c>fetch</c>, because the browser <c>EventSource</c> API
/// cannot set custom auth headers). Each event is a JSON <see cref="LiveUpdateEvent"/>
/// pushed as a <c>live-update</c> frame. For backward compatibility with the
/// previous client that only read <c>case-assignment</c>, an assignment event
/// ALSO emits a legacy <c>case-assignment</c> frame carrying the same payload.
///
/// On any mutation (case assignment/status/priority/comment, customer
/// profile/delete/restore, customer self-service profile edit), the UI updates
/// instantly instead of waiting for the list poll. SSE is native ASP.NET Core —
/// no SignalR package required.
/// </summary>
[ApiController]
[Route("api/cases")]
[Authorize(Roles = "Admin,Agent")]
public class CaseEventsController : ControllerBase
{
    private readonly ILiveUpdateHub _hub;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Mirror the API's enum/date serialization so the client parses identically.
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public CaseEventsController(ILiveUpdateHub hub)
    {
        _hub = hub;
    }

    /// <summary>
    /// Opens the SSE stream. Writes periodic comment keep-alives and pushes each
    /// <see cref="LiveUpdateEvent"/> as a <c>live-update</c> (and, for assignment,
    /// legacy <c>case-assignment</c>) frame. Cancels cleanly when the client
    /// disconnects (the <c>HttpContext.RequestAborted</c> token).
    /// </summary>
    [HttpGet("events")]
    [Produces("text/event-stream")]
    public async Task Events(CancellationToken cancellationToken)
    {
        var response = Response;
        response.Headers.Append("Content-Type", "text/event-stream");
        response.Headers.Append("Cache-Control", "no-cache");
        response.Headers.Append("Connection", "keep-alive");
        // Disable response buffering so frames flush immediately (instant push).
        response.Headers.Append("X-Accel-Buffering", "no");

        var writer = response.BodyWriter;

        // Signal the client the stream is live.
        await WriteFrameAsync(writer, ": connected\n\n", cancellationToken);

        // Keep-alive so proxies don't drop an idle connection between events.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));

        // Enumerate the unified channel concurrently with the keep-alive timer.
        var eventTask = PumpLiveAsync(writer, _hub.Subscribe(), cancellationToken);
        var keepAliveTask = PumpKeepAliveAsync(writer, timer, cancellationToken);

        await Task.WhenAny(eventTask, keepAliveTask);
        await eventTask;
        await keepAliveTask;
    }

    private async Task PumpLiveAsync(System.IO.Pipelines.PipeWriter writer, System.Threading.Channels.ChannelReader<LiveUpdateEvent> reader, CancellationToken ct)
    {
        try
        {
            await foreach (var evt in reader.ReadAllAsync(ct))
            {
                var json = JsonSerializer.Serialize(evt, JsonOptions);
                // Unified frame every consumer reads.
                await WriteFrameAsync(writer, $"event: live-update\ndata: {json}\n\n", ct);
                // Legacy frame for any client still wired to "case-assignment".
                if (string.Equals(evt.Kind, "case-assignment", StringComparison.Ordinal))
                {
                    await WriteFrameAsync(writer, $"event: case-assignment\ndata: {json}\n\n", ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — expected.
        }
    }

    private async Task PumpKeepAliveAsync(System.IO.Pipelines.PipeWriter writer, PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await WriteFrameAsync(writer, ": keep-alive\n\n", ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — expected.
        }
    }

    private static async Task WriteFrameAsync(System.IO.Pipelines.PipeWriter writer, string frame, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(frame);
        await writer.WriteAsync(bytes, ct);
        await writer.FlushAsync(ct);
    }
}

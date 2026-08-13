using System.Text;
using System.Text.Json;
using CustomerService.Application.Dtos;
using CustomerService.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CustomerService.Api.Controllers;

/// <summary>
/// Server-Sent Events (SSE) stream of case assignment changes. The frontend
/// connects with the staff JWT (sent as a Bearer header via a streaming
/// <c>fetch</c>, because the browser <c>EventSource</c> API cannot set custom
/// auth headers). Each event is a JSON <see cref="CaseEvent"/>.
///
/// On assignment/unassignment (admin side), the UI updates instantly instead of
/// waiting for the 30s list poll. SSE is native ASP.NET Core — no SignalR
/// package required.
/// </summary>
[ApiController]
[Route("api/cases")]
[Authorize(Roles = "Admin,Agent")]
public class CaseEventsController : ControllerBase
{
    private readonly ICaseEventHub _hub;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Mirror the API's enum/date serialization so the client parses identically.
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public CaseEventsController(ICaseEventHub hub)
    {
        _hub = hub;
    }

    /// <summary>
    /// Opens the SSE stream. Writes periodic comment keep-alives and pushes each
    /// <see cref="CaseEvent"/> as a <c>data:</c> frame. Cancels cleanly when the
    /// client disconnects (the <c>HttpContext.RequestAborted</c> token).
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

        // Enumerate the channel concurrently with the keep-alive timer.
        var eventTask = PumpEventsAsync(writer, cancellationToken);
        var keepAliveTask = PumpKeepAliveAsync(writer, timer, cancellationToken);

        await Task.WhenAny(eventTask, keepAliveTask);
        // WhenAny returns on first completion; if it was the event loop ending
        // (hub closed — never happens here) or cancellation, we just exit and
        // ASP.NET disposes the response.
        await eventTask;
        await keepAliveTask;
    }

    private async Task PumpEventsAsync(System.IO.Pipelines.PipeWriter writer, CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _hub.Reader.ReadAllAsync(ct))
            {
                var json = JsonSerializer.Serialize(evt, JsonOptions);
                await WriteFrameAsync(writer, $"event: case-assignment\ndata: {json}\n\n", ct);
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

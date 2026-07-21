namespace CustomerService.Application.Interfaces;

/// <summary>
/// Allows admins to verify SMTP connectivity and credentials without sending
/// a real email. Used by the <c>POST /api/email/test</c> endpoint.
/// </summary>
public interface IEmailTestService
{
    /// <summary>
    /// Attempts to connect to the SMTP server, authenticate, and optionally
    /// send a test message. Returns a diagnostic result.
    /// </summary>
    /// <param name="testRecipient">
    /// Optional recipient address. When provided, a real test email is sent.
    /// When <c>null</c>, only the connect + authenticate handshake is tested.
    /// </param>
    Task<EmailTestResult> TestSmtpAsync(string? testRecipient = null);
}

/// <summary>Diagnostic result of an SMTP test.</summary>
public sealed class EmailTestResult
{
    /// <summary>Whether the overall test succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>SMTP host that was tested.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>SMTP port that was tested.</summary>
    public int Port { get; set; }

    /// <summary>Sender email used for authentication.</summary>
    public string SenderEmail { get; set; } = string.Empty;

    /// <summary>Whether the CONNECT + AUTH handshake succeeded.</summary>
    public bool Connected { get; set; }

    /// <summary>Whether a test email was sent (only when testRecipient was provided).</summary>
    public bool? TestEmailSent { get; set; }

    /// <summary>Recipient the test email was sent to (dev-redirected if applicable).</summary>
    public string? SentTo { get; set; }

    /// <summary>Human-readable status or error message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Classification of any failure (e.g. AUTH_FAILED, NETWORK_IO).</summary>
    public string? ErrorCategory { get; set; }
}

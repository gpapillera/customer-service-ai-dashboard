using CustomerService.Application.Interfaces;
using CustomerService.Application.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CustomerService.Application.Services;

/// <summary>
/// Diagnostics service that validates SMTP connectivity and credentials
/// without affecting normal notification flows. Used by the admin
/// <c>POST /api/email/test</c> endpoint.
/// </summary>
public sealed class EmailTestService : IEmailTestService
{
    private readonly ILogger<EmailTestService> _logger;
    private readonly EmailOptions _emailOptions;

    public EmailTestService(ILogger<EmailTestService> logger, IOptions<EmailOptions> emailOptions)
    {
        _logger = logger;
        _emailOptions = emailOptions.Value;
    }

    /// <inheritdoc />
    public async Task<EmailTestResult> TestSmtpAsync(string? testRecipient = null)
    {
        var result = new EmailTestResult
        {
            Host = _emailOptions.SmtpHost,
            Port = _emailOptions.SmtpPort,
            SenderEmail = _emailOptions.SenderEmail
        };

        using var client = new SmtpClient();
        client.Timeout = 30_000; // 30 seconds

        try
        {
            // Step 1: Connect
            _logger.LogInformation("SMTP test: connecting to {Host}:{Port}...", _emailOptions.SmtpHost, _emailOptions.SmtpPort);
            await client.ConnectAsync(_emailOptions.SmtpHost, _emailOptions.SmtpPort, SecureSocketOptions.StartTls);
            _logger.LogInformation("SMTP test: connected. Now authenticating...");

            // Step 2: Authenticate
            await client.AuthenticateAsync(_emailOptions.SenderEmail, _emailOptions.SenderPassword);
            result.Connected = true;
            _logger.LogInformation("SMTP test: authenticated successfully.");

            // Step 3: Optionally send a test email
            if (!string.IsNullOrWhiteSpace(testRecipient))
            {
                _logger.LogInformation("SMTP test: sending test email to {Recipient}...", testRecipient);
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(_emailOptions.SenderDisplayName, _emailOptions.SenderEmail));
                message.To.Add(MailboxAddress.Parse(testRecipient));
                message.Subject = "[Customer Service AI] SMTP Test Email";
                message.Body = new TextPart("plain")
                {
                    Text = "This is an automated test email from the Customer Service AI dashboard.\n\n"
                         + $"Sent at: {DateTime.UtcNow:u}\n"
                         + $"SMTP Host: {_emailOptions.SmtpHost}:{_emailOptions.SmtpPort}\n"
                         + $"Sender: {_emailOptions.SenderEmail}\n\n"
                         + "If you received this, email notifications are working correctly."
                };
                await client.SendAsync(message);
                result.TestEmailSent = true;
                result.SentTo = testRecipient;
                _logger.LogInformation("SMTP test: test email sent to {Recipient}.", testRecipient);
            }

            await client.DisconnectAsync(true);
            result.Success = true;
            result.Message = result.TestEmailSent == true
                ? $"SMTP connection, authentication, and send all succeeded. Test email delivered to {testRecipient}."
                : "SMTP connection and authentication succeeded. No test email was sent (no recipient provided).";
        }
        catch (System.Security.Authentication.AuthenticationException ex)
        {
            _logger.LogError(ex, "SMTP test: authentication failed.");
            result.Connected = false;
            result.ErrorCategory = "AUTH_FAILED";
            result.Message = $"Authentication failed: {ex.Message}. Verify that SenderPassword is a valid Gmail App Password (not the account password). If 2FA was recently changed, generate a new App Password at https://myaccount.google.com/apppasswords.";
        }
        catch (System.IO.IOException ex)
        {
            _logger.LogError(ex, "SMTP test: network I/O error.");
            result.Connected = false;
            result.ErrorCategory = "NETWORK_IO";
            result.Message = $"Network error: {ex.Message}. Check that the SMTP host ({_emailOptions.SmtpHost}) is reachable from this server.";
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            _logger.LogError(ex, "SMTP test: socket error.");
            result.Connected = false;
            result.ErrorCategory = "SOCKET";
            result.Message = $"Socket error: {ex.Message}. DNS resolution or TCP connection to {_emailOptions.SmtpHost}:{_emailOptions.SmtpPort} failed.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMTP test: unexpected error.");
            result.Connected = false;
            result.ErrorCategory = "UNKNOWN";
            // Provide extra guidance for common Gmail error codes
            if (ex.Message.Contains("535", StringComparison.OrdinalIgnoreCase))
            {
                result.ErrorCategory = "SMTP_535";
                result.Message = "SMTP 535 error: authentication rejected. The Gmail App Password may be revoked or the account may require re-verification. Generate a new App Password at https://myaccount.google.com/apppasswords.";
            }
            else
            {
                result.Message = $"Unexpected error: {ex.Message}";
            }
        }

        return result;
    }
}

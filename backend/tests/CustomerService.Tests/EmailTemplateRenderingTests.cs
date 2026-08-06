using CustomerService.Application.Services;
using Xunit;

namespace CustomerService.Tests;

/// <summary>
/// Unit tests for <see cref="EmailNotificationSender.RenderTemplate"/> — token
/// substitution used when rendering editable, per-type email templates.
/// </summary>
public class EmailTemplateRenderingTests
{
    private static readonly Dictionary<string, string> Tokens = new()
    {
        ["customerName"] = "Juan Dela Cruz",
        ["agentName"] = "Grace Agent",
        ["caseId"] = "3",
        ["caseSubject"] = "API returning 500 errors",
        ["caseStatus"] = "Resolved",
    };

    [Fact]
    public void SubstitutesKnownTokens()
    {
        var subject = EmailNotificationSender.RenderTemplate(
            "Case #{{caseId}} is overdue: {{caseSubject}}", Tokens);
        var body = EmailNotificationSender.RenderTemplate(
            "Hello {{agentName}},\nCase {{caseId}}: {{caseSubject}}", Tokens);
        Assert.Equal("Case #3 is overdue: API returning 500 errors", subject);
        Assert.Equal("Hello Grace Agent,\nCase 3: API returning 500 errors", body);
    }

    [Fact]
    public void UnknownToken_LeftLiteral()
    {
        var subject = EmailNotificationSender.RenderTemplate(
            "Hi {{nonexistent}}", Tokens);
        var body = EmailNotificationSender.RenderTemplate(
            "Body {{customerName}}", Tokens);
        Assert.Equal("Hi {{nonexistent}}", subject);
        Assert.Equal("Body Juan Dela Cruz", body);
    }

    [Fact]
    public void MissingTokenInMap_SubstitutesWhatIsPresent()
    {
        var map = new Dictionary<string, string> { ["caseId"] = "7" };
        var subject = EmailNotificationSender.RenderTemplate(
            "Case #{{caseId}} update", map);
        Assert.Equal("Case #7 update", subject);
    }

    [Fact]
    public void EmptyTemplate_ReturnsEmptyString()
    {
        var result = EmailNotificationSender.RenderTemplate("", Tokens);
        Assert.Equal("", result);
    }
}

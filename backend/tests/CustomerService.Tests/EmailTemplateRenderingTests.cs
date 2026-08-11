using CustomerService.Application.Services;
using CustomerService.Domain.Entities;
using CustomerService.Infrastructure.Data;
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

    // --- EnsureActionLink: an activation/reset email without its link is dead ---

    [Fact]
    public void EnsureActionLink_AppendsWhenTemplateOmitsIt()
    {
        const string link = "http://localhost:4200/customer/accept-invite?token=abc123";
        var body = EmailNotificationSender.EnsureActionLink("Hello,\n\nWelcome.", link);
        Assert.Contains(link, body);
    }

    [Fact]
    public void EnsureActionLink_DoesNotDuplicateWhenTemplateAlreadyHasIt()
    {
        const string link = "http://localhost:4200/customer/accept-invite?token=abc123";
        var rendered = $"Hello,\n\nActivate here:\n\n{link}\n\nThanks";
        var body = EmailNotificationSender.EnsureActionLink(rendered, link);
        Assert.Equal(rendered, body);
    }

    [Fact]
    public void EnsureActionLink_NoLink_LeavesBodyUnchanged()
    {
        Assert.Equal("Hello", EmailNotificationSender.EnsureActionLink("Hello", null));
        Assert.Equal("Hello", EmailNotificationSender.EnsureActionLink("Hello", "  "));
    }

    [Fact]
    public void ActionLinkToken_RendersIntoSeedInviteTemplate()
    {
        const string link = "http://localhost:4200/customer/accept-invite?token=abc123";
        var map = new Dictionary<string, string> { ["actionLink"] = link };
        var body = EmailNotificationSender.RenderTemplate(
            "Hello,\n\n{{actionLink}}\n\nThanks", map);
        Assert.Contains(link, body);
        Assert.DoesNotContain("{{actionLink}}", body);
    }

    /// <summary>
    /// The real seed templates — not a copy — must carry {{actionLink}} for
    /// every type whose whole purpose is to deliver a clickable URL. A
    /// "set your password" email without its link is a dead end for the user.
    /// </summary>
    [Theory]
    [InlineData("CustomerInvite")]
    [InlineData("CustomerPasswordReset")]
    [InlineData("StaffPasswordReset")]
    public void SeedTemplate_CarriesActionLink(string type)
    {
        const string link = "http://localhost:4200/customer/accept-invite?token=abc123";
        var template = SeedData.EmailTemplates().Single(t => t.Type == type);
        var body = EmailNotificationSender.RenderTemplate(
            template.Body, new Dictionary<string, string> { ["actionLink"] = link });

        Assert.Contains(link, body);
        // Fresh-DB path renders the link itself, so the fallback must not double it.
        Assert.Equal(body, EmailNotificationSender.EnsureActionLink(body, link));
    }

    /// <summary>
    /// Admin-composed email is free text, not a 'Case #n "subject"' machine
    /// string. Quote-extraction there silently deletes everything outside the
    /// first quoted pair — an admin quoting a refund amount would lose the rest
    /// of the message.
    /// </summary>
    [Fact]
    public void AdminManual_KeepsFullMessageWhenItContainsQuotes()
    {
        const string typed = "Your refund of \"PHP 1,500\" has been approved and will arrive in 3 days.";
        Assert.Equal(typed, EmailNotificationSender.ResolveCaseSubject(
            NotificationType.AdminManual, typed));
    }

    [Fact]
    public void MachineMessage_StillExtractsQuotedSubject()
    {
        const string generated = "Case #7 \"Login blocked\" for Juan Dela Cruz is overdue.";
        Assert.Equal("Login blocked", EmailNotificationSender.ResolveCaseSubject(
            NotificationType.CaseOverdue, generated));
    }

    /// <summary>
    /// Legacy rows (and the resend path copying them) carry the URL only in
    /// Message, with Link NULL. The renderer discards Message, so the link must
    /// still be recoverable or the activation email goes out dead.
    /// </summary>
    [Fact]
    public void ResolveActionLink_FallsBackToUrlInMessage()
    {
        const string link = "http://localhost:4200/customer/accept-invite?token=920a5ab1";
        var n = new Notification
        {
            Link = null,
            Message = $"Click the link below to activate:\n\n{link}\n\nThis link expires in 48 hours.",
        };
        Assert.Equal(link, EmailNotificationSender.ResolveActionLink(n));
    }

    [Fact]
    public void ResolveActionLink_PrefersLinkColumnOverMessage()
    {
        var n = new Notification
        {
            Link = "http://localhost:4200/customer/accept-invite?token=NEW",
            Message = "old text http://localhost:4200/customer/accept-invite?token=OLD",
        };
        Assert.Equal("http://localhost:4200/customer/accept-invite?token=NEW",
            EmailNotificationSender.ResolveActionLink(n));
    }

    [Fact]
    public void ExtractFirstUrl_TrimsTrailingSentencePunctuation()
    {
        Assert.Equal("http://a.test/x",
            EmailNotificationSender.ExtractFirstUrl("Go to http://a.test/x."));
        Assert.Equal("https://a.test/y",
            EmailNotificationSender.ExtractFirstUrl("See https://a.test/y, then stop"));
    }

    [Fact]
    public void ExtractFirstUrl_NoUrl_ReturnsNull()
    {
        Assert.Null(EmailNotificationSender.ExtractFirstUrl("no link here"));
        Assert.Null(EmailNotificationSender.ExtractFirstUrl(null));
    }

    /// <summary>
    /// End-to-end reproduction of the exact failure the user reported: the DB
    /// still holds the OLD linkless CustomerInvite template, and the notification
    /// row (a legacy/resent invite) has Link NULL with the URL only in Message.
    /// The delivered body must still greet the customer by name AND contain the
    /// activation link.
    /// </summary>
    [Fact]
    public void LegacyInvite_WithOldDbTemplate_StillRendersNameAndLink()
    {
        // Verbatim from the live DB (EmailTemplates.Type='CustomerInvite').
        const string oldDbTemplate =
            "Hello {{customerName}},\n\nYou've been invited to the Customer Portal.\n\n"
            + "If you weren't expecting this invitation, you can safely ignore this email.\n\n"
            + "Thank you,\nCustomer Service Team";
        const string link =
            "http://localhost:4200/customer/accept-invite?token=920a5ab149bf4524aef87969ebd5d8ba";

        var n = new Notification
        {
            Type = NotificationType.CustomerInvite,
            Recipient = "glenpapillera@gmail.com",
            Link = null,
            Message = "You've been invited to set up your secure customer portal account. "
                + $"Click the link below to choose a password and activate your account:\n\n{link}\n\n"
                + "This link expires in 48 hours.",
        };

        // customerName is resolved by recipient lookup inside the sender.
        var rendered = EmailNotificationSender.RenderTemplate(
            oldDbTemplate,
            new Dictionary<string, string> { ["customerName"] = "Glen Papillera" });
        var body = EmailNotificationSender.EnsureActionLink(
            rendered, EmailNotificationSender.ResolveActionLink(n));

        Assert.Contains("Hello Glen Papillera,", body);
        Assert.DoesNotContain("Hello ,", body);
        Assert.Contains(link, body);
    }

    /// <summary>
    /// F1 regression: for account-activation / password-reset notifications the
    /// {{portalLink}} token must resolve to the per-recipient deep link (the
    /// invite/reset URL), NOT the bare portal homepage. Before the fix a
    /// template using {{portalLink}} rendered "http://localhost:4200" and the
    /// EnsureActionLink safety net then re-appended the real link at the bottom.
    /// </summary>
    [Fact]
    public void ResolvePortalLink_AccountTypesUseDeepLink_NotHomepage()
    {
        const string link = "http://localhost:4200/customer/accept-invite?token=abc123";
        Assert.Equal(link, EmailNotificationSender.ResolvePortalLink(
            NotificationType.CustomerInvite, link, "http://localhost:4200"));
        Assert.Equal(link, EmailNotificationSender.ResolvePortalLink(
            NotificationType.CustomerPasswordReset, link, "http://localhost:4200"));
        Assert.Equal(link, EmailNotificationSender.ResolvePortalLink(
            NotificationType.StaffPasswordReset, link, "http://localhost:4200"));
    }

    [Fact]
    public void ResolvePortalLink_CaseTypesUseHomepage()
    {
        const string link = "http://localhost:4200/customer/accept-invite?token=abc123";
        Assert.Equal("http://localhost:4200", EmailNotificationSender.ResolvePortalLink(
            NotificationType.CaseOverdue, link, "http://localhost:4200"));
        Assert.Equal("http://localhost:4200", EmailNotificationSender.ResolvePortalLink(
            NotificationType.CaseResolved, link, "http://localhost:4200"));
        Assert.Equal("http://localhost:4200", EmailNotificationSender.ResolvePortalLink(
            NotificationType.AdminManual, link, "http://localhost:4200"));
    }

    /// <summary>
    /// End-to-end: an operator template that uses {{portalLink}} for a
    /// CustomerInvite must render the full action URL exactly once — no homepage
    /// placeholder, and no duplicate appended at the bottom by EnsureActionLink.
    /// </summary>
    [Fact]
    public void AccountInvite_PortalLinkRendersFullLink_Once()
    {
        const string link = "http://localhost:4200/customer/accept-invite?token=920a5ab1";
        const string template =
            "Hello {{customerName}},\n\n"
            + "Set your password here:\n\n{{portalLink}}\n\n"
            + "This link expires in 48 hours.";

        var tokenMap = new Dictionary<string, string>
        {
            ["portalLink"] = EmailNotificationSender.ResolvePortalLink(
                NotificationType.CustomerInvite, link, "http://localhost:4200"),
            ["customerName"] = "Link Test User",
        };
        var rendered = EmailNotificationSender.RenderTemplate(template, tokenMap);
        var body = EmailNotificationSender.EnsureActionLink(rendered, link);

        Assert.Contains(link, body);
        Assert.DoesNotContain("http://localhost:4200\n", body); // no bare homepage line
        // The link appears exactly once (template render + no duplicate append).
        Assert.Equal(1, body.Split(new[] { link }, StringSplitOptions.None).Length - 1);
    }
}

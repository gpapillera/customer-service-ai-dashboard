using CustomerService.Application.Services;
using Xunit;

namespace CustomerService.Tests;

/// <summary>
/// Unit tests for recipient-domain routing in <see cref="EmailNotificationSender"/>.
/// A recipient whose domain is on the allowed list receives mail directly;
/// everyone else is redirected to the configured test address.
/// </summary>
public class EmailRoutingTests
{
    private static readonly HashSet<string> Allowed = new()
    {
        "gmail.com", "yahoo.com", "outlook.com",
    };

    [Fact]
    public void ListedDomain_ReturnsOriginalRecipient()
    {
        var result = EmailNotificationSender.ResolveEffectiveRecipient(
            "someone@gmail.com", Allowed, "glnppllr@gmail.com");
        Assert.Equal("someone@gmail.com", result);
    }

    [Fact]
    public void NonListedDomain_RedirectsToTestAddress()
    {
        var result = EmailNotificationSender.ResolveEffectiveRecipient(
            "customer@acme.ph", Allowed, "glnppllr@gmail.com");
        Assert.Equal("glnppllr@gmail.com", result);
    }

    [Fact]
    public void EmptyOriginal_RedirectsToTestAddress()
    {
        var result = EmailNotificationSender.ResolveEffectiveRecipient(
            "", Allowed, "glnppllr@gmail.com");
        Assert.Equal("glnppllr@gmail.com", result);
    }

    [Fact]
    public void SubdomainOfListed_IsNotMatched()
    {
        // "mail.gmail.com" is NOT "gmail.com" — only exact domain suffix matches.
        var result = EmailNotificationSender.ResolveEffectiveRecipient(
            "a@mail.gmail.com", Allowed, "glnppllr@gmail.com");
        Assert.Equal("glnppllr@gmail.com", result);
    }

    [Fact]
    public void ExactListedDomainWithSubaddressing_Matches()
    {
        // Plus-addressing keeps the same domain; should deliver directly.
        var result = EmailNotificationSender.ResolveEffectiveRecipient(
            "someone+label@yahoo.com", Allowed, "glnppllr@gmail.com");
        Assert.Equal("someone+label@yahoo.com", result);
    }
}

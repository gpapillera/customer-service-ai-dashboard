using CustomerService.Domain.Entities;
using Xunit;

namespace CustomerService.Tests;

/// <summary>
/// Unit tests for the soft-delete / purge audit fields on <see cref="Customer"/>.
/// </summary>
public class CustomerSoftDeleteTests
{
    [Fact]
    public void NewCustomer_HasDefaultSoftDeleteAndPurgeState()
    {
        var c = new Customer { Name = "X", Email = "x@y.z" };

        Assert.False(c.IsDeleted);
        Assert.False(c.Purged);
        Assert.Null(c.DeletedAtUtc);
        Assert.Null(c.PurgedAtUtc);
    }
}

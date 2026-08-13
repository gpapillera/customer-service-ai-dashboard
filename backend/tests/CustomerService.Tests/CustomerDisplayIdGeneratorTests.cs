using CustomerService.Application.Interfaces;
using CustomerService.Application.Services;

namespace CustomerService.Tests;

/// <summary>
/// Unit tests for the monotonic customer display-ID sequence. Locks the
/// contract that replaced the old "C-{Id}" derivation: values are unique,
/// formatted as C-NNNNN, and never reuse a number freed by a deleted row.
/// </summary>
public class CustomerDisplayIdGeneratorTests
{
    [Fact]
    public void Next_IncrementsFromOne_WhenNotSeeded()
    {
        ICustomerDisplayIdGenerator gen = new CustomerDisplayIdGenerator();
        Assert.Equal("C-00001", gen.Next());
        Assert.Equal("C-00002", gen.Next());
        Assert.Equal("C-00003", gen.Next());
    }

    [Fact]
    public void SeedFrom_ContinuesAboveHighestExistingSuffix()
    {
        ICustomerDisplayIdGenerator gen = new CustomerDisplayIdGenerator();
        // Existing rows C-00001..C-00011 (mirrors seed data).
        var existing = Enumerable.Range(1, 11).Select(i => $"C-{i:D5}");
        gen.SeedFrom(existing);
        // Next must be above the highest existing suffix, not start at 1.
        Assert.Equal("C-00012", gen.Next());
        Assert.Equal("C-00013", gen.Next());
    }

    [Fact]
    public void SeedFrom_IgnoresNullsAndForeignFormats()
    {
        ICustomerDisplayIdGenerator gen = new CustomerDisplayIdGenerator();
        gen.SeedFrom(new[] { null, "", "C-00005", "not-a-display-id", "X-00009" });
        // Highest parseable suffix is 5, so next is 6.
        Assert.Equal("C-00006", gen.Next());
    }

    [Fact]
    public void Next_ProducesUniqueValues_UnderConcurrentCalls()
    {
        ICustomerDisplayIdGenerator gen = new CustomerDisplayIdGenerator();
        gen.SeedFrom(new[] { "C-00099" });

        var results = new System.Collections.Concurrent.ConcurrentBag<string>();
        var tasks = Enumerable.Range(0, 50).Select(_ =>
            Task.Run(() => results.Add(gen.Next()))).ToArray();
        Task.WaitAll(tasks.ToArray());

        // 50 generated values must all be distinct (no two threads got the same ID).
        Assert.Equal(50, results.Distinct().Count());
        // And they must all sit above the seeded 99.
        Assert.All(results, id => Assert.True(string.CompareOrdinal(id, "C-00099") > 0));
    }
}

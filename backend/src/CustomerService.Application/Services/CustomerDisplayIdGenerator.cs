using System.Globalization;
using CustomerService.Application.Interfaces;

namespace CustomerService.Application.Services;

/// <summary>
/// In-process monotonic sequence generator for customer display IDs of the form
/// <c>C-NNNNN</c>. The counter is seeded from the highest existing suffix found
/// in the database at startup (<see cref="SeedFrom"/>) and then only ever
/// increments, so generated values are unique and never reuse a freed number.
/// Thread-safe: the counter advance is guarded by a lock so concurrent customer
/// creations under the scoped service graph can't hand out the same ID.
/// </summary>
public sealed class CustomerDisplayIdGenerator : ICustomerDisplayIdGenerator
{
    // Format used for every generated value, e.g. "C-00012".
    private const string Prefix = "C-";
    private const int Width = 5;

    private readonly object _lock = new();
    private int _next = 1;
    private bool _seeded;

    /// <inheritdoc/>
    public void SeedFrom(IEnumerable<string?>? existingIds)
    {
        var highest = 0;
        if (existingIds is not null)
        {
            foreach (var raw in existingIds)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var suffix = raw!.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
                    ? raw.Substring(Prefix.Length)
                    : raw;
                if (int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > highest)
                {
                    highest = value;
                }
            }
        }

        lock (_lock)
        {
            // Begin one above the highest existing suffix (min 1 if none exist).
            _next = highest + 1;
            _seeded = true;
        }
    }

    /// <inheritdoc/>
    public string Next()
    {
        int value;
        lock (_lock)
        {
            if (!_seeded)
            {
                // Defensive: if Next is called before SeedFrom, start the sequence
                // at 1 rather than throwing — SeedFrom is the documented contract.
                _seeded = true;
            }
            value = _next++;
        }
        return Prefix + value.ToString(CultureInfo.InvariantCulture).PadLeft(Width, '0');
    }
}

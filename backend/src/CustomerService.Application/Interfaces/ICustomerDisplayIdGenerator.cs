namespace CustomerService.Application.Interfaces;

/// <summary>
/// Produces unique, human-readable customer display IDs (e.g. <c>C-00012</c>) in
/// a strictly monotonic sequence. Unlike deriving the ID from the row's integer
/// primary key, a sequence never reuses a value freed by a deleted customer and
/// never collides with an existing row — the counter is seeded from the highest
/// suffix already present at startup and only ever increments from there.
/// Registered as a singleton so the running count is shared across the process.
/// </summary>
public interface ICustomerDisplayIdGenerator
{
    /// <summary>
    /// Seeds the counter from the set of display IDs currently in the database so
    /// the next generated value continues above the highest existing suffix. Call
    /// exactly once at startup, before any <see cref="Next"/> invocation.
    /// </summary>
    /// <param name="existingIds">Existing <c>CustomerDisplayId</c> values (may contain nulls / other formats).</param>
    void SeedFrom(IEnumerable<string?>? existingIds);

    /// <summary>Returns the next unique display ID in the sequence (e.g. <c>C-00013</c>).</summary>
    string Next();
}

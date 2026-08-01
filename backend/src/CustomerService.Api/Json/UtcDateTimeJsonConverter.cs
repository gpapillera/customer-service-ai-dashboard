using System.Text.Json;
using System.Text.Json.Serialization;

namespace CustomerService.Api.Json;

/// <summary>
/// Serializes <see cref="DateTime"/> values as UTC instants (ISO-8601 with a
/// trailing "Z").
/// EF Core returns <see cref="DateTimeKind.Unspecified"/> after a database
/// round-trip (SQLite/SQL Server store no kind), which otherwise makes
/// System.Text.Json emit a timezone-naive string (no "Z"). The frontend then
/// parses the naive string as local time while date-only inputs ("YYYY-MM-DD")
/// parse as UTC midnight — causing date-filter boundary mismatches.
/// Every *Utc column in the domain is written from <see cref="DateTime.UtcNow"/>,
/// so treating Unspecified values as UTC is always correct here.
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    /// <inheritdoc/>
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDateTime();

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            // EF Core materializes stored values as Unspecified; the column is
            // named *Utc and always written from DateTime.UtcNow, so treat it
            // as UTC rather than leaving it naive.
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
        writer.WriteStringValue(utc);
    }
}

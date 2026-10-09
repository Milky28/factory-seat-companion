using System.Text.Json;

namespace FactorySeatCompanion.Core;

public interface IFactorySeatReader
{
    Task<BriefingRead> ReadAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

public sealed class ProvisionalJsonBriefingReader(string path, TimeSpan maxAge) : IFactorySeatReader
{
    public const string ContractName = "factory-seat-companion-provisional";
    public const int MaximumBytes = 1024 * 1024;

    // Read anew each cycle: catches atomic replacements and partial writes without watcher races.
    // No last-good fallback; invalid or missing files revoke readiness immediately.
    public async Task<BriefingRead> ReadAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
            if (stream.Length > MaximumBytes) return new(SourceStatus.Invalid, null, "Briefing exceeds 1 MiB.");
            var json = await BoundedJson.ReadAsync(stream, MaximumBytes, cancellationToken);
            return Parse(json, now, maxAge);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(SourceStatus.Unavailable, null, $"Briefing read failed: {ex.Message}");
        }
    }

    public static BriefingRead Parse(string json, DateTimeOffset now, TimeSpan maxAge)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (JsonRead.HasDuplicateProperties(document.RootElement))
                return new(SourceStatus.Invalid, null, "Duplicate briefing properties.");
            var briefing = JsonSerializer.Deserialize<ProvisionalBriefing>(json, JsonDefaults.Options);
            if (briefing is null || briefing.Contract != ContractName || briefing.SchemaVersion != 1)
                return new(SourceStatus.Invalid, null, "Unsupported provisional contract/schemaVersion. This is not a career save reader.");
            if (briefing.GeneratedAt is not { } generated || generated > now.AddSeconds(30))
                return new(SourceStatus.Invalid, null, "Missing or future generatedAt timestamp.");
            if (now - generated > maxAge) return new(SourceStatus.Stale, briefing, "Briefing is stale; refresh it at the source.");
            if (briefing.State is not ("Upcoming" or "Armed"))
                return new(SourceStatus.Invalid, briefing, "Round must be Upcoming or Armed.");
            return new(SourceStatus.Available, briefing, "Provisional briefing loaded.");
        }
        catch (JsonException ex) { return new(SourceStatus.Invalid, null, $"Invalid briefing JSON: {ex.Message}"); }
    }
}

internal static class BoundedJson
{
    public static async Task<string> ReadAsync(Stream stream, int maximumBytes, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, token)) > 0)
        {
            if (buffer.Length + count > maximumBytes) throw new IOException("JSON response exceeds size limit.");
            buffer.Write(chunk, 0, count);
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray()).TrimStart('\uFEFF');
    }
}

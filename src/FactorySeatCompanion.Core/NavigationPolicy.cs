using System.Text.Json;

namespace FactorySeatCompanion.Core;

public sealed record NavigationDecision(bool Safe, string? State, string Diagnostic);

public static class NavigationPolicy
{
    public static NavigationDecision Evaluate(string? json, CompanionOptions options)
    {
        if (json is null) return new(false, null, "Navigation unavailable; garage polling disabled.");
        try
        {
            using var document = JsonDocument.Parse(json);
            if (JsonRead.HasDuplicateProperties(document.RootElement)) return new(false, null, "Ambiguous navigation properties.");
            if (HasReplayOrLoading(document.RootElement)) return new(false, null, "Replay/loading detected; garage polling disabled.");
            var element = document.RootElement;
            foreach (var segment in options.NavigationStatePointer.Split('/').Skip(1))
            {
                var key = segment.Replace("~1", "/").Replace("~0", "~");
                if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(key, out element))
                    return new(false, null, "Navigation state pointer missing; garage polling disabled.");
            }
            if (element.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                return new(false, null, "Navigation state pointer does not reference a single value; garage polling disabled.");
            var state = JsonRead.Text(element);
            bool safe = state is not null && options.SafeSetupStates.Contains(state, StringComparer.Ordinal);
            return new(safe, state, safe ? "Configured setup state." : "Navigation state not explicitly allowed; garage polling disabled.");
        }
        catch (JsonException) { return new(false, null, "Invalid navigation JSON; garage polling disabled."); }
    }

    private static bool HasReplayOrLoading(JsonElement element, int nestedJsonDepth = 0)
    {
        if (element.ValueKind == JsonValueKind.String)
            return element.GetString()?.Contains("replay", StringComparison.OrdinalIgnoreCase) == true;
        if (element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray().Any(e => HasReplayOrLoading(e, nestedJsonDepth));
        if (element.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals("isReplay", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("replay", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("isLoading", StringComparison.OrdinalIgnoreCase))
            {
                // Only an explicit false clears these flags; unsupported flag encodings fail closed.
                if (property.Value.ValueKind != JsonValueKind.False) return true;
            }
            // Live LMU reports loading as loadingStatus.loading; it sends no isReplay/isLoading.
            if (property.Name.Equals("loadingStatus", StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.Object &&
                property.Value.TryGetProperty("loading", out var loading) && loading.ValueKind != JsonValueKind.False) return true;
            if (property.Name.Equals("loadingData", StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
                if (nestedJsonDepth >= 3) return true;
                try
                {
                    using var nested = JsonDocument.Parse(property.Value.GetString()!);
                    if (JsonRead.HasDuplicateProperties(nested.RootElement) || HasReplayOrLoading(nested.RootElement, nestedJsonDepth + 1)) return true;
                }
                catch (JsonException) { return true; }
                // Inspect the decoded flags, not the raw JSON text: the string
                // {"isReplay":false} contains the word replay but is not a replay.
                continue;
            }
            if (HasReplayOrLoading(property.Value, nestedJsonDepth)) return true;
        }
        return false;
    }
}

using System.Text.Json;

namespace FactorySeatCompanion.Core;

// Which part of LMU the player is in, as far as /navigation/state can tell. LMU's menu app keeps
// its page to itself, so the main menu and race setup are both Menu (live probe, 2026-10-10).
public enum LmuScreen { Unknown, Menu, Loading, InSession }

public static class LmuScreenClassifier
{
    // Live values: menus GSTATE_SETUP + NAV_MAIN_MENU; loading GSTATE_INIT/GSTATE_RESTART + NAV_EVENT
    // with loadingStatus.loading true; garage/monitor NAV_EVENT; driving, pause and replay NAV_REALTIME;
    // leaving a session GSTATE_POST.
    public static LmuScreen Classify(string? navigationJson)
    {
        if (navigationJson is null) return LmuScreen.Unknown;
        try
        {
            using var document = JsonDocument.Parse(navigationJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return LmuScreen.Unknown;
            if (root.TryGetProperty("loadingStatus", out var loadingStatus) && loadingStatus.ValueKind == JsonValueKind.Object &&
                loadingStatus.TryGetProperty("loading", out var loading) && loading.ValueKind == JsonValueKind.True)
                return LmuScreen.Loading;
            if (!root.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.Object) return LmuScreen.Unknown;
            string? game = Text(state, "gameState"), navigation = Text(state, "navigationState");
            if (game is "GSTATE_INIT" or "GSTATE_RESTART") return LmuScreen.Loading;
            if (navigation is "NAV_EVENT" or "NAV_REALTIME" || game is "GSTATE_DYN" or "GSTATE_POST") return LmuScreen.InSession;
            if (game == "GSTATE_SETUP" && navigation == "NAV_MAIN_MENU") return LmuScreen.Menu;
            return LmuScreen.Unknown;
        }
        catch (JsonException) { return LmuScreen.Unknown; }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

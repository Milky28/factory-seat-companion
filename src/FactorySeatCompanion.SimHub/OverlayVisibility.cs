using System;

namespace FactorySeatCompanion.SimHub;

// Decides FactorySeatPlugin.ShowOverlay. LMU reports the main menu and race setup identically, so the
// overlay shows anywhere in the menus and hides from loading until the player is back in the menus.
// An unknown screen (LMU not reachable, a stale report) shows it too, so the checklist stays up when the
// API breaks. The ToggleOverlay action flips the default until LMU moves to another known screen, so a
// dismissal on the main menu lasts until the next session.
// Kept free of SimHub types so the tests can compile it under net8.0.
public sealed class OverlayVisibility
{
    public const string Unknown = "Unknown";
    private readonly object gate = new object();
    private string screen = Unknown;
    private string lastKnown = Unknown;
    private bool toggled;

    public string Screen { get { lock (gate) return screen; } }

    public bool Visible
    {
        get
        {
            lock (gate)
            {
                bool shown = screen != "Loading" && screen != "InSession";
                return toggled ? !shown : shown;
            }
        }
    }

    // The screen a report vouches for: only a fresh one, matching DashboardState's freshness rule.
    public static string ScreenOf(CompanionMessage? message, DateTimeOffset now, TimeSpan maxAge)
    {
        var report = message?.Report;
        if (report is null || string.IsNullOrEmpty(message!.Screen)) return Unknown;
        var age = now - report.ObservedAt;
        return age <= maxAge && age >= TimeSpan.FromSeconds(-1) ? message.Screen : Unknown;
    }

    public void Observe(string current)
    {
        lock (gate)
        {
            screen = string.IsNullOrEmpty(current) ? Unknown : current;
            if (screen == Unknown || screen == lastKnown) return;
            lastKnown = screen;
            toggled = false;
        }
    }

    public void Toggle()
    {
        lock (gate) toggled = !toggled;
    }
}

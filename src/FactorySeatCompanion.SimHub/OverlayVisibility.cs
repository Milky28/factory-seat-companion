using System;

namespace FactorySeatCompanion.SimHub;

// Decides FactorySeatPlugin.ShowOverlay. LMU reports the main menu and race setup identically, so the
// overlay shows anywhere in the menus and hides from loading until the player is back in the menus.
// It stays hidden while LMU's process isn't running (NotRunning) and while LMU starts up, until its menus
// are first seen. An unknown screen after that (API not answering, a stale report) keeps it showing, so the
// checklist stays up when the API breaks in the menus. The
// ToggleOverlay action flips the default until LMU moves to another known screen, so a dismissal on the
// main menu lasts until the next session.
// Kept free of SimHub types so the tests can compile it under net8.0.
public sealed class OverlayVisibility
{
    public const string Unknown = "Unknown";
    public const string NotRunning = "NotRunning";
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
                bool shown = screen == "Menu" || (screen == Unknown && lastKnown == "Menu");
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

    public void Observe(string current, bool lmuRunning = true)
    {
        lock (gate)
        {
            screen = !lmuRunning ? NotRunning : string.IsNullOrEmpty(current) ? Unknown : current;
            // A brief Unknown read keeps a toggle, except when LMU has just started.
            if (screen == lastKnown || (screen == Unknown && lastKnown != NotRunning)) return;
            lastKnown = screen;
            toggled = false;
        }
    }

    public void Toggle()
    {
        lock (gate) toggled = !toggled;
    }
}

// Whether Le Mans Ultimate's process is running, checked at most every two seconds because
// enumerating processes is too slow for SimHub's per-frame property reads.
public sealed class LmuProcess
{
    public const string ProcessName = "Le Mans Ultimate";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);
    private readonly object gate = new object();
    private readonly Func<bool> probe;
    private DateTimeOffset checkedAt = DateTimeOffset.MinValue;
    private bool running;

    public LmuProcess(Func<bool>? probe = null) { this.probe = probe ?? Probe; }

    public bool IsRunning(DateTimeOffset now)
    {
        lock (gate)
        {
            if (now - checkedAt >= CheckInterval || now < checkedAt)
            {
                running = probe();
                checkedAt = now;
            }
            return running;
        }
    }

    private static bool Probe()
    {
        var processes = System.Diagnostics.Process.GetProcessesByName(ProcessName);
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }
}

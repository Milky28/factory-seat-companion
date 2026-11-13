using System;
using System.IO;

namespace FactorySeatCompanion.SimHub;

// Factory Seat 1.5's start-weekend.request: the one file Companion writes, and only when the driver
// presses the StartRaceWeekend action. Its text is the career's id from briefing.json; Factory Seat
// deletes it and starts the weekend if that career is open with its round Upcoming.
// Kept free of SimHub and serializer types so the tests can compile it under net8.0.
public static class StartWeekendRequest
{
    public const string FileName = "start-weekend.request";

    // Why no request should be written, or null when it should. Mirrors Factory Seat's own checks,
    // so a press that couldn't start anything never leaves a file behind.
    public static string? Refusal(bool factorySeatRunning, bool? open, string? roundState, string? careerId)
    {
        if (!factorySeatRunning) return "Factory Seat is not running.";
        if (open != true) return "No career is open in Factory Seat.";
        if (roundState == "Armed") return "The race weekend is already started.";
        if (roundState != "Upcoming") return "No round is waiting to start.";
        if (string.IsNullOrWhiteSpace(careerId)) return "The briefing has no career id.";
        return null;
    }

    // Written to a temporary name and renamed into place, so Factory Seat never reads half a file.
    public static void Write(string folder, string careerId)
    {
        string target = Path.Combine(folder, FileName);
        string temp = target + ".tmp";
        File.WriteAllText(temp, careerId.Trim());
        try { File.Move(temp, target); }
        catch (IOException) when (File.Exists(target))
        {
            // An earlier press Factory Seat hasn't picked up yet: this one replaces it.
            File.Delete(target);
            File.Move(temp, target);
        }
    }
}

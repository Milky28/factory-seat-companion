using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace FactorySeatCompanion.SimHub;

// Runs the net8.0 CLI (which hosts the core validator) as a child process and keeps its latest
// report. SimHub's net48 runtime cannot load the net8.0 core directly; see docs/simhub-plugin.md.
// All process and parsing work stays here, off SimHub's DataUpdate path.
internal sealed class CompanionBridge : IDisposable
{
    private const int MaxLineChars = 1024 * 1024;
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);

    private readonly PluginSettings settings;
    private readonly string defaultCliPath;
    private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
    private CompanionMessage? latest;
    private string status = "Not started";
    private string? lastError;
    private Task? supervisor;

    public CompanionBridge(PluginSettings settings, string defaultCliPath)
    {
        this.settings = settings;
        this.defaultCliPath = defaultCliPath;
    }

    public CompanionMessage? Latest => Volatile.Read(ref latest);
    public string Status => Volatile.Read(ref status);

    public void Start() => supervisor = Task.Run(() => SuperviseAsync(cancellation.Token));

    private void SetStatus(string value) => Volatile.Write(ref status, value);

    private async Task SuperviseAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                RunOnce(token);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                SetStatus("Companion failed to start: " + ex.Message);
            }
            // Earlier evidence is revoked whenever the bridge is not running.
            Volatile.Write(ref latest, null);
            try { await Task.Delay(RestartDelay, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void RunOnce(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(settings.BriefingPath))
        {
            SetStatus("Set BriefingPath in the Factory Seat Companion plugin settings.");
            return;
        }
        string cliPath = string.IsNullOrWhiteSpace(settings.CliPath) ? defaultCliPath : settings.CliPath;
        if (!File.Exists(cliPath))
        {
            SetStatus("Companion CLI not found: " + cliPath);
            return;
        }

        var args = new List<string> { "--jsonl", "--exit-when-stdin-closes", "--briefing", settings.BriefingPath };
        if (!string.IsNullOrWhiteSpace(settings.ConfigPath)) args.AddRange(new[] { "--config", settings.ConfigPath });
        if (!string.IsNullOrWhiteSpace(settings.FixturesPath)) args.AddRange(new[] { "--fixtures", settings.FixturesPath });
        var info = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(cliPath)) ?? "",
        };
        if (cliPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            info.FileName = "dotnet";
            args.Insert(0, cliPath);
        }
        else info.FileName = cliPath;
        info.Arguments = WindowsCommandLine.Join(args);

        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => OnOutput(e.Data);
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) Volatile.Write(ref lastError, e.Data);
        };
        Volatile.Write(ref lastError, null);
        process.Start();
        SetStatus("Running");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        // Stdin stays open; closing it (or SimHub exiting) tells the CLI to stop.
        using (token.Register(() => Stop(process)))
            process.WaitForExit();
        if (!token.IsCancellationRequested)
            SetStatus(string.Format("Companion exited with code {0}: {1} Restarting.",
                process.ExitCode, Volatile.Read(ref lastError) ?? "no error output."));
    }

    private void OnOutput(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        if (line!.Length > MaxLineChars)
        {
            Volatile.Write(ref latest, null);
            SetStatus("Companion output line too long; report discarded.");
            return;
        }
        try
        {
            var message = JsonConvert.DeserializeObject<CompanionMessage>(line);
            Volatile.Write(ref latest, message?.Report is null ? null : message);
            SetStatus("Running");
        }
        catch (JsonException ex)
        {
            Volatile.Write(ref latest, null);
            SetStatus("Unreadable companion output: " + ex.Message);
        }
    }

    private static void Stop(Process process)
    {
        try
        {
            process.StandardInput.Close();
            if (!process.WaitForExit(2000)) process.Kill();
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    public void Dispose()
    {
        cancellation.Cancel();
        try { supervisor?.Wait(TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { }
        cancellation.Dispose();
    }
}

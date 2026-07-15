using System.Runtime.InteropServices;
using Avalonia;
using Bennewitz.Ninja.ClaudeForge.Core.Backup;
using Bennewitz.Ninja.ClaudeForge.Core.Platform;
using Bennewitz.Ninja.ClaudeForge.Localization;
using Bennewitz.Ninja.ClaudeForge.Services;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Localization;
using Serilog;

namespace Bennewitz.Ninja.ClaudeForge;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 1. Debug flags — parse the command-line FIRST so the --culture
        //    flag is available to LocalizationService.ApplyCulture() in
        //    Step 2.  Initialize() does NOT emit Serilog logs (the pipeline
        //    isn't configured yet); deferred warnings + the active-flags
        //    summary are flushed by LogActiveFlags() in Step 4 below.
        DebugFlags.Initialize(args);

        // 2. Culture — must run before any UI is constructed: Semi.Avalonia
        //    reads CultureInfo.CurrentUICulture to pick its locale bundle.
        //    DefaultThreadCurrent* propagates to future thread-pool threads
        //    automatically.  CultureOverride is the validated --culture value
        //    (null if no flag, or if the flag value failed validation —
        //    falls back to OS default in either case).
        LocalizationService.ApplyCulture(DebugFlags.CultureOverride);

        // 2b. Wire the library-side PropertyEditorWrapper's chrome strings
        //     (Reset button label / tooltip, lock-icon tooltip, etc.) to the
        //     App's localised resx.  The library wrapper is rarely rendered
        //     in ClaudeForge — the App-side wrapper at Controls/PropertyEditorWrapper.axaml
        //     handles the primary editor surface — but the library wrapper
        //     ships as a fallback and its 7 chrome strings need to localise
        //     when it DOES render (e.g. via external library consumers, or
        //     via the deferred LEAF-EDITORS-4.2 consolidation).  Must run
        //     before any wrapper XAML is parsed (the {x:Static} markup
        //     extension dereferences at parse time and caches the value).
        WrapperStrings.Resolver = key => key switch
        {
            nameof(WrapperStrings.TipResetToInherited) => Strings.TipResetToInherited,
            nameof(WrapperStrings.TipReadOnly) => Strings.TipManagedLocked,
            nameof(WrapperStrings.TipUndocumented) => Strings.TipUndocumented,
            nameof(WrapperStrings.TipShowSuggestions) => Strings.TipShowSuggestions,
            nameof(WrapperStrings.TipNewSetting) => Strings.TipNewSetting,
            nameof(WrapperStrings.LabelOverridden) => Strings.TextOverridden,
            nameof(WrapperStrings.LabelReset) => Strings.ButtonReset,
            var _ => key,
        };

        // 3. AppDomain crash handler — catches fatal exceptions on non-Avalonia threads
        //    (e.g. background threads that crash before the UI thread even starts) and
        //    true-fatal errors during CLR initialisation. The Avalonia runtime is likely
        //    dead at this point, so we use the native-OS dialog rather than the in-process
        //    FatalErrorDialog.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Exception? ex = e.ExceptionObject as Exception;
            Log.Fatal(ex, "AppDomain.UnhandledException (IsTerminating={IsTerm})", e.IsTerminating);
            AvaloniaDiagnostics.ShowNativeFatalError(ex?.ToString() ?? "Unknown fatal error.");
        };

        // 4. One-line logging pipeline: rolling file sink (8h buckets, 3d retention) +
        //    Trace + F12 live-log window + Avalonia logger bridge. All toggles left at
        //    their safe defaults; only AppName + LogsDirectory are mandatory.
        AvaloniaDiagnostics.ConfigureLogging(new AvaloniaDiagnosticsOptions
        {
            AppName = "ClaudeForge",
            LogsDirectory = PlatformPaths.AppLogsDirectory,
            // Second live-tail window (opt-in): streams debounced ConfigFileWatcher
            // hits so the user can watch external edits (Claude CLI, other editors)
            // to the settings files in real time. Fed by MainWindowViewModel via
            // AvaloniaDiagnostics.EnqueueEvent; launched from the F12 window header
            // link or Shift+F12.
            EnableEventTailWindow = true,
            EventTailWindowTitle = "Live Config-File Events — Shift+F12 to hide",
            EventTailLaunchLabel = "Config-file events ▸",
        });

        // 5. Flush any deferred debug-flag warnings (e.g. invalid --culture
        //    value, unknown flag list) + the active-flags summary line.
        //    Couldn't log these inside DebugFlags.Initialize because that
        //    runs before ConfigureLogging.
        DebugFlags.LogActiveFlags();

        // 6. CLI tools that bypass the GUI bootstrap.  When the user passes
        // a maintenance flag like --cleanup-restore-sidecars, we run the
        // tool, print + log a summary, and return without ever calling
        // BuildAvaloniaApp().  Detected case-insensitively to match the
        // rest of the CLI surface (DebugFlags is also case-insensitive).
        foreach (string arg in args)
        {
            if (string.Equals(arg, "--cleanup-restore-sidecars", StringComparison.OrdinalIgnoreCase))
            {
                RunRestoreSidecarCleanup();
                return;
            }
        }

        // 7. Boot.
        try
        {
            Log.Information("Starting ClaudeForge v{Version}",
                typeof(Program).Assembly.GetName().Version);
            // emit the resolved log directory once on startup
            // so users (especially on Linux) can find the rolling .txt files
            // without spelunking the deploy tree.  Stderr because stdout may
            // be closed in some launch contexts (e.g. detached desktop launch).
            Console.Error.WriteLine($"[ClaudeForge] log directory: {PlatformPaths.AppLogsDirectory}");
            Log.Information("Log directory: {LogDir}", PlatformPaths.AppLogsDirectory);
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fatal error during Avalonia bootstrap");

            // Try the Avalonia FatalErrorDialog first (works when the runtime is partially alive).
            AvaloniaDiagnostics.ShowFatalErrorDialog(
                "ClaudeForge encountered a fatal error during startup.", ex);

            // Then show the native OS dialog as an additional fallback (covers the case where
            // Avalonia is completely unusable and FatalErrorDialog.ShowSafe silently no-ops).
            AvaloniaDiagnostics.ShowNativeFatalError(ex.ToString());
        }
        finally
        {
            // Permanent: the very last log line before the Serilog pipeline
            // shuts down.  Pairs with the matching "Starting ClaudeForge"
            // line at the top of try and the [App.Shutdown] line emitted by
            // MainWindow.OnClosed in the normal-quit path.  Together they
            // bracket the session so post-mortems can tell "did the app
            // crash mid-flight?" (Starting present, Exiting absent) from
            // "did the user quit cleanly?" (both lines present, plus the
            // [App.Shutdown] one in between).
            Log.Information("Exiting ClaudeForge");
            Log.CloseAndFlush();
        }
    }

    // LogToTrace() deliberately not called: Avalonia log events reach Trace through the
    // Serilog pipeline (SerilogAvaloniaSink → WriteTo.Trace()), so adding LogToTrace()
    // here would double-emit them. The debugger Output window keeps working as before.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
                         .UsePlatformDetect()
                         .WithInterFont();
    }

    /// <summary>
    /// Dispatch for the <c>--cleanup-restore-sidecars</c> command-line
    /// flag.  Walks <c>~/.claude/</c>, deletes every <c>*.bak</c> file
    /// left behind by <see cref="Bennewitz.Ninja.ClaudeForge.Core.Backup.RestoreEngine"/>'s
    /// pre-restore sidecar pattern, and prints a human-readable summary
    /// to stderr (visible regardless of how the binary was launched) plus
    /// a Serilog entry for post-mortem grepping.
    /// <para>
    /// CLI-only by design: the operation is unconditionally destructive
    /// and best performed as an explicit maintenance step the user has
    /// consciously chosen.  GUI does not expose this — see the comment
    /// on <see cref="Bennewitz.Ninja.ClaudeForge.Core.Backup.RestoreSidecarCleanup"/>
    /// for the full rationale.
    /// </para>
    /// </summary>
    /// <summary>
    /// Attach to the parent process's console so <see cref="Console.Out"/>
    /// and <see cref="Console.Error"/> writes flow back to the terminal
    /// the binary was launched from.  Required because
    /// <c>&lt;OutputType&gt;WinExe&lt;/OutputType&gt;</c> in the csproj
    /// makes the linker mark the binary GUI-subsystem on Windows, which
    /// detaches it from the parent console at startup — any
    /// <c>Console.WriteLine</c> would otherwise vanish silently.
    /// <para>
    /// No-op on non-Windows: the WinExe subsystem flag has no effect
    /// outside Windows, and Linux / macOS launches keep stdout / stderr
    /// connected to the parent terminal by default.
    /// </para>
    /// <para>
    /// Best-effort: an unsuccessful attach (e.g. the binary was launched
    /// from an environment with no console — Explorer double-click,
    /// Task Scheduler) is silently ignored.  The cleanup result still
    /// lands in the Serilog rolling log either way.
    /// </para>
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int dwProcessId);

    private const int ATTACH_PARENT_PROCESS = -1;

    private static void TryAttachParentConsole()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            AttachConsole(ATTACH_PARENT_PROCESS);
        }
        catch
        {
            // Best-effort: any failure (P/Invoke unavailable on a future
            // trimmed config, parent has no console, etc.) leaves
            // Console.Out / Console.Error unattached.  The Serilog log
            // line still records the run, so the user can verify via
            // the rolling log if they don't see terminal output.
        }
    }

    private static void RunRestoreSidecarCleanup()
    {
        // Reattach to the parent terminal so the user sees progress.
        // Without this the WinExe-detached console swallows everything.
        TryAttachParentConsole();

        Console.Error.WriteLine($"[ClaudeForge] Cleaning up *.bak restore sidecars under {PlatformPaths.ClaudeHome}…");
        Log.Information("[Cleanup] Restore-sidecar cleanup invoked via --cleanup-restore-sidecars");

        RestoreSidecarCleanup.Result result = RestoreSidecarCleanup.Run(onProgress: count =>
        {
            // Heartbeat every 1000 deletions so the user knows the run is
            // making progress on a large directory (the user's reported
            // baseline was ~99 000 sidecars; without progress the binary
            // looks hung).
            Console.Error.WriteLine($"  ...{count:N0} deleted");
        });

        double mb = result.BytesReclaimed / (1024.0 * 1024.0);
        string summary =
            $"Scanned {result.FilesScanned:N0} *.bak file(s); " +
            $"deleted {result.FilesDeleted:N0} ({mb:N1} MB reclaimed); " +
            $"{result.Failures} failure(s).";

        Console.Error.WriteLine($"[ClaudeForge] {summary}");
        Log.Information("[Cleanup] {Summary}", summary);

        if (result.Failures > 0)
        {
            Console.Error.WriteLine("[ClaudeForge] First failures (capped at 20):");
            foreach (string msg in result.FailureMessages)
            {
                Console.Error.WriteLine($"  {msg}");
                Log.Warning("[Cleanup] {Msg}", msg);
            }
        }

        Log.CloseAndFlush();
    }
}
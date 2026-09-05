# LayeredEditors.Avalonia.Diagnostics

Logging and diagnostics for Avalonia desktop apps, wired in three calls: a Serilog pipeline
with a bucketed rolling file sink, a floating live-log window on F12, crash and notice dialogs
that survive a dead UI thread, and a logger for the binding errors Avalonia's own logger never
reports. Trim-safe; no reflection-based configuration.

## Wiring

```csharp
// Program.Main, before BuildAvaloniaApp(): the pipeline must exist before any
// Avalonia type touches the logger.
AvaloniaDiagnostics.ConfigureLogging(new AvaloniaDiagnosticsOptions
{
    AppName = "MyApp",
    LogsDirectory = Path.Combine(appDataDirectory, "logs"),
});

// App.OnFrameworkInitializationCompleted, after base: creates the hidden
// live-log window and installs the binding-error logger.
AvaloniaDiagnostics.InstallAvaloniaHooks();

// MainWindow.OnKeyDown: F12 shows or hides the live-log window. Inside the
// window, F12 hides it again.
if (e.Key == Key.F12)
{
    AvaloniaDiagnostics.ToggleLiveLogWindow();
    e.Handled = true;
}
```

Crash handlers call `AvaloniaDiagnostics.ShowFatalErrorDialog(message, exception)` for a
dialog with a copyable stack trace, or `ShowNativeFatalError(message)` when the Avalonia
runtime itself is gone. `ShowNonFatalNotice(title, message, body)` is the modeless notice with
a copyable body.

## What is in the box

| Piece | Purpose |
|---|---|
| `AvaloniaDiagnostics` | The bootstrap: `ConfigureLogging`, `InstallAvaloniaHooks`, `ToggleLiveLogWindow`, the dialog helpers, `CurrentLogFilePath` |
| `AvaloniaDiagnosticsOptions` | App name, logs directory, minimum level, bucket size and retention, and a switch for every piece below |
| `BucketedRollingFileSink` | One file per time bucket (8 h by default), old buckets deleted past the retention (3 d by default) |
| `SerilogAvaloniaSink` | Routes Avalonia's internal logger into Serilog, with an area mute list |
| `LiveLogWindow` | The F12 window: bounded channel, coalesced updates, row copy, links to the current log file and folder |
| `LiveTailWindow` | An optional second live window for host events, fed by `AvaloniaDiagnostics.EnqueueEvent` and reachable from the live-log window's header |
| `FatalErrorDialog`, `NonFatalNoticeDialog`, `NativeErrorDialog` | Crash and notice surfaces, from a full Avalonia dialog down to the native message box |
| `BindingValidationErrorLogger` | Logs the coercion errors that land in `DataValidationErrors` and bypass `Avalonia.Logging` |

Every piece is optional through `AvaloniaDiagnosticsOptions`; only `AppName` and
`LogsDirectory` are required.

## Where it comes from

Extracted from [ClaudeForge](https://github.com/JanusMael/ClaudeForge), which runs it in
production. Issues and pull requests go there.

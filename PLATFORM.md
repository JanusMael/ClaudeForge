# Platform abstraction

Reference for the `IPlatformInfo` abstraction that lets ClaudeForge branch
on OS identity for **UI display purposes** without scattering raw
`RuntimeInformation.IsOSPlatform` checks across the codebase, and the
`--windows` / `--macos` / `--linux` debug flags that emulate a different
platform on the host machine for testing.

This doc covers:

- [Why this exists](#why-this-exists)
- [The contract](#the-contract)
- [What emulation covers vs what it does not](#what-emulation-covers-vs-what-it-does-not)
- [Production code path](#production-code-path)
- [Debug-flag override path](#debug-flag-override-path)
- [Adding a new property](#adding-a-new-property)
- [Refactoring a call site to use the abstraction](#refactoring-a-call-site-to-use-the-abstraction)
- [Testing code that depends on `PlatformInfo.Current`](#testing-code-that-depends-on-platforminfocurrent)
- [Frequently asked decisions](#frequently-asked-decisions)

---

## Why this exists

Two motivations:

1. **Centralised platform identity.** Before this abstraction the codebase
   had ~23 distinct call sites doing `RuntimeInformation.IsOSPlatform(...)`
   or `OperatingSystem.IsWindows()` for a mix of reasons — some genuinely
   needed to gate a Windows-registry call, others were just choosing a
   path style for a UI label. Mixing the two cases made it hard to reason
   about which checks were *load-bearing* and which were just *display*.

2. **Cross-platform UI testing.** When you are working on the Windows
   build but want to verify how the macOS Desktop config path string
   renders in the About page, you used to need to actually boot macOS.
   The `--macos` debug flag now flips `PlatformInfo.Current` to an
   emulated macOS, and every UI surface that consults the abstraction
   (the path-resolution helpers in `PlatformPaths`, the platform tag
   written into backup manifests, etc.) reports as macOS for the rest
   of the session.

The abstraction is **deliberately scoped** to UI / path / display
behavior. Real platform-intrinsic APIs (Windows registry, MSIX
junctions, Unix shell execution) keep their direct
`OperatingSystem.IsWindows()` guards because emulating them would just
fail later, deeper in the stack, with worse error messages than the
honest "feature unavailable" path. See
[What emulation covers vs what it does not](#what-emulation-covers-vs-what-it-does-not)
for the full list.

---

## The contract

Defined in `src/ClaudeForge.Core/Platform/IPlatformInfo.cs`:

```csharp
public interface IPlatformInfo
{
    bool   IsWindows         { get; }
    bool   IsMacOS           { get; }
    bool   IsLinux           { get; }
    string PlatformId        { get; }   // "windows" | "macos" | "linux" | "unknown"
    string DisplayName       { get; }   // "Windows" | "macOS" | "Linux" | "Unknown"
    char   PathListSeparator { get; }   // ';' on Windows, ':' elsewhere
    StringComparison PathComparison { get; }  // OrdinalIgnoreCase on Windows, Ordinal elsewhere
}
```

Every property is read-only and idempotent — the platform identity does
not change at runtime.

| Property | Used by |
|---|---|
| `IsWindows` / `IsMacOS` / `IsLinux` | Branch selection in `PlatformPaths.DesktopConfigPath`, `PlatformPaths.DesktopLogsPath`. Future call sites that pick a path layout, command string, or label per platform. |
| `PlatformId` | Backup manifest tags, structured log fields, About page. |
| `DisplayName` | About page, error dialogs, anywhere a human-readable platform name needs to appear. |
| `PathListSeparator` | Splitting a `PATH`-style env var for display or for "is this dir on PATH?" membership checks. |
| `PathComparison` | Filesystem-path equality (NTFS is case-insensitive in practice; Linux/macOS-typical filesystems are case-sensitive). Used for path-list deduplication. |

---

## What emulation covers vs what it does not

### Covered (eligible for `PlatformInfo.Current` redirection)

These call sites **already** consult `PlatformInfo.Current` and therefore
respect the `--windows` / `--macos` / `--linux` debug flag:

| Call site | What it changes |
|---|---|
| `PlatformPaths.PlatformId` | The string written into backup manifests (`"windows"` / `"macos"` / `"linux"`). Useful when generating manifest-comparison tests for cross-platform restore. |
| `PlatformPaths.DesktopConfigPath` | The Desktop config file path shown on the About page and in the Backup section. Emulated macOS shows `~/Library/Application Support/Claude/...`; emulated Linux shows `~/.config/Claude/...`. |
| `PlatformPaths.DesktopLogsPath` | The Desktop logs path; returns `null` for emulated Linux because Claude Desktop has no persistent log dir on Linux. |
| `InstallCommandViewModel.ForClaudeCode` | The shell one-liner shown in the install banner (`irm https://claude.ai/install.ps1 \| iex` on emulated Windows; `curl -fsSL https://claude.ai/install.sh \| bash` on emulated macOS / Linux) and the Run-button label (PowerShell vs Terminal). The button itself still launches the real host's terminal — emulation is purely for the displayed copy-paste content. |

Surfaces that **could** be redirected but are not yet refactored — open a
PR if you need them under emulation:

- `ShellLauncher` command-string selection (osascript / explorer / xdg-open
  *names* only; the actual exec calls are not redirectable).
- `NativeErrorDialog` tool selection (Windows MessageBox vs macOS osascript
  vs Linux zenity).
- `ProductVersionProbe` shell-wrapper selection (`cmd.exe /c` for `.cmd`,
  PowerShell for `.ps1`).
- `AdditionalDirectoriesResolver` path-comparison case-sensitivity.
- `EnvironmentEditorViewModel` env-var-name regex (Windows `COMSPEC`
  vs Unix `TMPDIR` family).

### Not covered (and why)

These call sites continue to use `OperatingSystem.IsWindows()` /
`RuntimeInformation.IsOSPlatform(...)` directly because the underlying
APIs are platform-intrinsic — emulating them would fail later, deeper,
with a worse error message:

| Call site | Why direct check stays |
|---|---|
| `ClaudeDesktopVersionProbe` (registry, AppX manifest, MSIX file paths) | The Windows registry literally does not exist on Linux/macOS; `Microsoft.Win32.Registry` throws `PlatformNotSupportedException`. |
| `MsixPathProbe` (NTFS junction creation, MSIX virtualised paths) | MSIX is a Windows feature. NTFS junctions need `mklink` which is Windows-only. |
| `EnvironmentEditorViewModel` (Machine / User env-var scope writes) | `EnvironmentVariableTarget.Machine` and `.User` are no-ops on Unix. |
| `BackupRestoreViewModel.MsixFixCommand` | Surfaces a Windows-specific PowerShell remediation step. |
| `ShellLauncher` actual `Process.Start` invocations | osascript on Windows or explorer.exe on Linux genuinely cannot run. |

The `IPlatformInfo` XML doc enumerates this so call-site authors know
which surfaces are eligible for redirection.

---

## Production code path

`src/ClaudeForge.Core/Platform/PlatformInfo.cs`:

```csharp
public static class PlatformInfo
{
    private static IPlatformInfo _current = RuntimePlatformInfo.Instance;
    public static IPlatformInfo Current => _current;
    public static void OverrideForDebug(IPlatformInfo info) => _current = info;
    public static void ResetForTesting()                    => _current = RuntimePlatformInfo.Instance;
}

public sealed class RuntimePlatformInfo : IPlatformInfo
{
    public static IPlatformInfo Instance { get; } = new RuntimePlatformInfo();
    public bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public bool IsMacOS   => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    public bool IsLinux   => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    // ...
}
```

Production code reads `PlatformInfo.Current.IsWindows` (etc.). The default
backing implementation, `RuntimePlatformInfo`, is a thin wrapper around
`RuntimeInformation.IsOSPlatform` with no caching — the JIT inlines the
underlying check, so the indirection costs nothing measurable.

---

## Debug-flag override path

`Program.Main` calls `DebugFlags.Initialize(args)` immediately after
`ConfigureLogging`:

```csharp
public static void Main(string[] args)
{
    LocalizationService.ApplyCulture();
    AppDomain.CurrentDomain.UnhandledException += /* ... */;
    AvaloniaDiagnostics.ConfigureLogging(/* ... */);

    DebugFlags.Initialize(args);   // ← parses --windows / --macos / --linux

    BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
}
```

`DebugFlags.Initialize` (in `src/ClaudeForge/Services/DebugFlags.cs`)
walks `args`, lowercases each, and dispatches:

```csharp
case "--linux":
    EmulatedPlatform = "linux";
    PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("linux"));
    break;
```

After that point, any subsequent `PlatformInfo.Current.IsLinux` returns
`true`, even on a Windows or macOS host.

`EmulatedPlatformInfo.ForId(id)` validates the id (`"windows"`, `"macos"`,
or `"linux"` — others throw `ArgumentOutOfRangeException`) and constructs
an immutable instance with the appropriate flags, separator, and
comparison.

When any flag is set, a single Serilog line records what's active so
captured logs always say which flags shaped the session:

```
[DebugFlags] active: --showInstallBanner, --linux
```

### Available flags

| Flag | Effect |
|---|---|
| `--showInstallBanner` | Force the install-guidance banner on top of MainWindow even when Claude Code or Claude Desktop is detected. |
| `--windows` | Emulate Windows: `PlatformInfo.Current.IsWindows = true`. |
| `--macos` | Emulate macOS. |
| `--linux` | Emulate Linux. |
| `--debug-help` (or `--help-debug`) | Log the available flags. |

> **Note:** there is intentionally no `--showWelcomeView` flag. Click the
> **Claude Code** or **Claude Desktop** header rows in the navigation
> tree to clear the active editor and surface the welcome view. See
> `MainWindowViewModel.OnSelectedNodeChanged`.

Flags are case-insensitive. Unknown args are silently ignored — Avalonia's
own args (`StartWithClassicDesktopLifetime(args)` reuses them) and
debugger-injected args pass through untouched. If multiple platform flags
are passed, the last one wins.

### Caveat: host environment is still real

The override flips the **branch selection** inside `PlatformPaths` and
similar helpers, but the underlying `Environment.GetFolderPath(...)` /
`Environment.GetEnvironmentVariable(...)` calls still resolve against
the **real** host. So an emulated-macOS path on a Windows host renders
as something like:

```
C:\Users\brian/Library/Application Support/Claude/claude_desktop_config.json
```

The structure is the macOS layout (`Library/Application Support/...`)
but the root is the Windows home dir. **For UI-display testing this is
exactly what you want** — the path *shape* is correct. For testing
filesystem operations against real macOS layouts, you still need a real
macOS host.

---

## Adding a new property

Use this checklist when extending `IPlatformInfo`:

1. **Add the property to `IPlatformInfo`** with a concise XML doc summary:

   ```csharp
   /// <summary>True when the (effective) platform supports POSIX file modes.</summary>
   bool SupportsPosixFileModes { get; }
   ```

2. **Implement it in `RuntimePlatformInfo`** by delegating to
   `RuntimeInformation` / `OperatingSystem` / a pure constant:

   ```csharp
   public bool SupportsPosixFileModes => !IsWindows;
   ```

3. **Implement it in `EmulatedPlatformInfo`** by branching on the stored
   identity:

   ```csharp
   public bool SupportsPosixFileModes => !IsWindows;  // Mac and Linux
   ```

4. **Add a unit test in `PlatformInfoTests`** for each emulated id.

5. **Refactor the call site(s)** to read `PlatformInfo.Current.SupportsPosixFileModes`
   instead of an inline `RuntimeInformation` check.

---

## Refactoring a call site to use the abstraction

Before:

```csharp
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    // Windows path layout
}
else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
{
    // macOS path layout
}
else
{
    // Linux fallback
}
```

After:

```csharp
if (PlatformInfo.Current.IsWindows)
{
    // Windows path layout
}
else if (PlatformInfo.Current.IsMacOS)
{
    // macOS path layout
}
else
{
    // Linux fallback
}
```

Add a comment explaining **why** the call was redirected — without it the
next reader cannot tell whether you are gating a real OS API or just
selecting a UI string. Example from `PlatformPaths.DesktopLogsPath`:

```csharp
// Use PlatformInfo.Current rather than RuntimeInformation directly so
// the --windows / --macos / --linux debug flags can emulate a different
// platform's path layout for UI testing without rebooting into that OS.
// The host's Environment.SpecialFolder lookups still resolve against
// the real OS — emulation only flips the branch selected here.
```

**When to NOT redirect:** if the branch fronts a Windows-only API
(`Microsoft.Win32.Registry`, `EnvironmentVariableTarget.Machine`,
`mklink` junctions, MSIX virtualisation), keep the direct
`OperatingSystem.IsWindows()` check. Add a one-line comment saying so —
this signals to future maintainers that the lack of `PlatformInfo` here
is deliberate, not an oversight:

```csharp
// Direct OperatingSystem check — Windows registry access is platform-intrinsic
// and cannot be redirected through PlatformInfo. See PLATFORM.md.
if (!OperatingSystem.IsWindows()) return null;
```

---

## Testing code that depends on `PlatformInfo.Current`

`PlatformInfo` holds **process-global mutable static state** (the
`_current` field). Tests must isolate themselves so emulation set in one
test does not bleed into the next.

**Pattern:** call `PlatformInfo.ResetForTesting()` in `TestCleanup`:

```csharp
[TestClass]
public sealed class MyFeatureTests
{
    [TestCleanup]
    public void Cleanup() => PlatformInfo.ResetForTesting();

    [TestMethod]
    public void Feature_OnEmulatedMacOS_RendersMacPath()
    {
        PlatformInfo.OverrideForDebug(EmulatedPlatformInfo.ForId("macos"));

        var path = MyFeature.DescribePath();

        StringAssert.Contains(path, "Library/Application Support");
    }
}
```

If a test mutates `DebugFlags` (which in turn mutates `PlatformInfo`),
call `DebugFlags.ResetForTesting()` instead — it resets every flag and
also resets `PlatformInfo`. See `tests/ClaudeForge.Tests/Services/DebugFlagsTests.cs`
for the canonical pattern.

**Thread safety:** `PlatformInfo._current` is intentionally NOT
thread-safe. The override is meant to run once in `Program.Main` before
any UI thread starts, and to be stable for the rest of the process.
Tests serialize through MSTest's per-test isolation. If you ever need
to mutate it from multiple threads at once, redesign the test rather
than adding locks here.

---

## Frequently asked decisions

### Why a static accessor, not dependency injection?

The constructors of view-models, services, and helpers in this codebase
are not currently routed through a DI container. Adding `IPlatformInfo`
as a constructor parameter to every consumer would touch dozens of files
for a property that is process-global and never changes after startup.
A static accessor with a clearly-marked `OverrideForDebug` hook gives
the same emulation power without the construction-graph churn.

### Why is `EmulatedPlatformInfo` not in `ClaudeForge` (the App project)?

It lives in `ClaudeForge.Core.Platform` next to `IPlatformInfo` so the
`Core` project can be self-tested with emulation in
`ClaudeForge.Core.Tests`. If `EmulatedPlatformInfo` were in the App
layer, Core tests would have to duplicate it, drifting from the
production behaviour over time.

### Why does `EmulatedPlatformInfo.ForId` throw on unknown ids?

A typo like `--linus` would silently leave the runtime as Windows, and
nobody would notice until a test surface looked wrong. Throwing makes
the typo crash startup with a clear `ArgumentOutOfRangeException`. The
parser in `DebugFlags.Initialize` only constructs the emulator for the
three valid ids, so production code never sees the exception path.

### Why log `[DebugFlags] active: …`?

Bug reports that include log captures usually omit the command line that
launched the build. Recording the active flags at startup means we can
always reproduce the user's session-shape from the log alone.

### Can I use this in production code (non-debug paths)?

Yes — `PlatformInfo.Current` is the recommended way to ask "what
platform are we on?" for any UI / display / path-shaping purpose. The
`OverrideForDebug` hook is the only debug-specific surface; reading
`Current` is fine in production.

The hard rule is: **never use `OverrideForDebug` from non-debug code**.
If you find yourself wanting to, you actually want a different
abstraction (likely a strategy pattern in the calling type, not a
process-global override). File an issue first.

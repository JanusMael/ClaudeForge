using Bennewitz.Ninja.AgentForge.Core.Schema;
using Avalonia;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Localization;
using Bennewitz.Ninja.OpenCodeForge.Localization;
using Serilog;

namespace Bennewitz.Ninja.OpenCodeForge;

/// <summary>Process entry point.</summary>
public static class Program
{
    /// <summary>
    /// Honour <c>--schema-source &lt;bundled|fetched&gt;</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ <b>One flag, deliberately not a flag framework.</b> ClaudeForge has a whole
    /// <c>DebugFlags</c> class with deferred warnings, a <c>--debug-help</c> listing and a reset
    /// hook; this app has no flags at all, and building that machinery to carry a single switch
    /// would be the larger mistake. When a second flag arrives, extract it then — and the shape
    /// to copy is ClaudeForge's, not this.
    /// </para>
    /// <para>
    /// PEEK rather than consume, matching the sibling: the value set is closed, so a rejected
    /// token may itself be a valid argument and must reach Avalonia rather than be swallowed.
    /// </para>
    /// <para>
    /// ⚠ Runs AFTER ConfigureLogging, unlike ClaudeForge's equivalent — nothing here affects
    /// logging or culture, so there is no reason to parse before the log exists, and every
    /// reason to want the rejection visible in it.
    /// </para>
    /// </remarks>
    private static void ApplySchemaSourceOverride(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], "--schema-source", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = args[i + 1];
            if (string.Equals(value, "bundled", StringComparison.OrdinalIgnoreCase))
            {
                SchemaRegistry.ProcessSourceOverride = SchemaSourceOverride.Bundled;
                Log.Information("[DebugFlags] --schema-source bundled: the network will not be used");
            }
            else if (string.Equals(value, "fetched", StringComparison.OrdinalIgnoreCase))
            {
                SchemaRegistry.ProcessSourceOverride = SchemaSourceOverride.Fetched;
                Log.Information(
                    "[DebugFlags] --schema-source fetched: a failed fetch will be FATAL, not fall back");
            }
            else
            {
                Log.Warning(
                    "[DebugFlags] --schema-source {Value} rejected: expected bundled or fetched. "
                    + "Using the normal loading chain.", value);
            }

            return;
        }
    }

    /// <summary>Start the app.</summary>
    [STAThread]
    public static void Main(string[] args)
    {
        ConfigureLogging();
        WireWrapperLocalization();
        ApplySchemaSourceOverride(args);

        try
        {
            Log.Information("Starting {App} v{Version}",
                Strings.AppTitle,
                typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown");

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // Log before rethrowing: an unhandled Avalonia startup failure otherwise leaves no
            // trace anywhere, since the window that would have shown it never appeared.
            Log.Fatal(ex, "Unhandled exception during startup");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    /// <summary>Avalonia's app builder. Also used by the headless test harness.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// Point the editor library's chrome strings at this app's resources.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ <b>Must run before any editor XAML is parsed.</b> The wrapper reads these through
    /// <c>{x:Static}</c>, which is dereferenced at parse time and cached, so wiring this after the
    /// first editor renders has no effect on what the user sees.
    /// </para>
    /// <para>
    /// Wiring it is not optional in spirit: unwired, the library falls back to its own English
    /// defaults, which are product-neutral but untranslated. Keys this app has no resource for
    /// fall through to that default rather than rendering empty.
    /// </para>
    /// </remarks>
    private static void WireWrapperLocalization()
    {
        Func<string, string> fallback = WrapperStrings.Resolver;

        WrapperStrings.Resolver = key => key switch
        {
            // This app has no translated chrome strings yet, so every key intentionally falls
            // through to the library's neutral English. The hook exists — and is tested — so that
            // adding a resource here is the only step needed later.
            var _ => fallback(key),
        };
    }

    private static void ConfigureLogging()
    {
        // Beside the executable, matching the sibling app, so a bug report can pick up the log
        // next to the binary that produced it.
        string logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(logDir, "opencodeforge-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                shared: true)
            .CreateLogger();
    }
}

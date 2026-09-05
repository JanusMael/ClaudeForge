using Avalonia;
using Avalonia.Headless;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.Tests;

[assembly: AvaloniaTestApplication(typeof(HeadlessTestApp))]

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.Tests;

/// <summary>
/// Minimal Avalonia application for the headless window tests in this assembly: dispatcher,
/// layout and input, no rendering. Tests obtain the shared session through
/// <see cref="HeadlessUnitTestSession.GetOrStartForAssembly"/> and dispatch a synchronous
/// body onto its UI thread — an awaiting body would make the test inert (see ClaudeForge's
/// <c>AGENTS.md</c>).
/// </summary>
public sealed class HeadlessTestApp : Application
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<HeadlessTestApp>()
                         .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
    }
}

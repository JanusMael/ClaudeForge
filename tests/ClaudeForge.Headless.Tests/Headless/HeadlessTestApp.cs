using Avalonia;
using Avalonia.Headless;
using Bennewitz.Ninja.ClaudeForge.Tests.Headless;

[assembly: AvaloniaTestApplication(typeof(HeadlessTestApp))]

namespace Bennewitz.Ninja.ClaudeForge.Tests.Headless;

/// <summary>
/// Minimal Avalonia application used by <see cref="Avalonia.Headless"/>
/// tests.  The real <see cref="Bennewitz.Ninja.ClaudeForge.App"/> can't be reused as-is —
/// it merges Semi.Avalonia + AvaloniaEdit Fluent + the App's resource
/// dictionaries, all of which assume a full GPU-backed AppBuilder.  Tests
/// use this stripped-down App that only loads the bare minimum needed for
/// dispatcher-driven scenarios (file watcher races, dialog modal flows,
/// async reload assertions).
/// </summary>
/// <remarks>
/// (file-watcher
/// races, dialog window races, backup-during-reload).  Test authors call
/// <see cref="HeadlessUnitTestSession.GetOrStartForAssembly"/> to obtain
/// the shared session and dispatch onto the headless UI thread.  See
/// <see cref="SampleHeadlessTests"/> for the canonical pattern.
/// </remarks>
public sealed class HeadlessTestApp : Application
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<HeadlessTestApp>()
                         .UseHeadless(new AvaloniaHeadlessPlatformOptions
                         {
                             // FrameBufferFormat = PixelFormat.Rgba8888  // Skia required for true rendering;
                             // we only need dispatcher + layout, not pixels.
                             UseHeadlessDrawing = true,
                         });
    }
}
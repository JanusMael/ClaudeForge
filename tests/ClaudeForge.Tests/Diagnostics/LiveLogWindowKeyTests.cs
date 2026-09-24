using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Bennewitz.Ninja.ClaudeForge.Diagnostics;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Diagnostics;

/// <summary>
/// The live-log window's title promises "F12 to hide", and the host's F12 handler on its main
/// window never sees a key pressed while the log window has focus. The window therefore
/// handles plain F12 itself; a modified F12 is left to the host.
/// </summary>
public sealed class LiveLogWindowKeyTests
{
    private static HeadlessUnitTestSession Session =>
        HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

    [Fact]
    public Task F12_InsideTheWindow_HidesIt_AndShiftF12_IsLeftToTheHost()
    {
        return Session.Dispatch(() =>
        {
            LiveLogWindow.Initialize();
            Window window = LiveLogWindow.WindowForTesting
                            ?? throw new Xunit.Sdk.XunitException("Initialize should have built the window.");

            LiveLogWindow.ToggleWindow();
            Assert.True(window.IsVisible, "ToggleWindow shows the hidden window.");

            window.KeyPress(Key.F12, RawInputModifiers.None, PhysicalKey.F12, null);
            Assert.False(window.IsVisible, "Plain F12 inside the window hides it.");

            LiveLogWindow.ToggleWindow();
            Assert.True(window.IsVisible);

            window.KeyPress(Key.F12, RawInputModifiers.Shift, PhysicalKey.F12, null);
            Assert.True(window.IsVisible, "A modified F12 is the host's chord and must not hide the window.");

            LiveLogWindow.ToggleWindow();
            Assert.False(window.IsVisible, "The host's toggle still hides it.");
        }, CancellationToken.None);
    }
}

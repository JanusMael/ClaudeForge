using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.UI;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.Tests;

/// <summary>
/// The live-log window's title promises "F12 to hide", and the host's F12 handler on its main
/// window never sees a key pressed while the log window has focus. The window therefore
/// handles plain F12 itself; a modified F12 is left to the host.
/// </summary>
[TestClass]
public sealed class LiveLogWindowKeyTests
{
    private static HeadlessUnitTestSession Session =>
        HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

    [TestMethod]
    public Task F12_InsideTheWindow_HidesIt_AndShiftF12_IsLeftToTheHost()
    {
        return Session.Dispatch(() =>
        {
            LiveLogWindow.Initialize();
            Window window = LiveLogWindow.WindowForTesting
                            ?? throw new AssertFailedException("Initialize should have built the window.");

            LiveLogWindow.ToggleWindow();
            Assert.IsTrue(window.IsVisible, "ToggleWindow shows the hidden window.");

            window.KeyPress(Key.F12, RawInputModifiers.None, PhysicalKey.F12, null);
            Assert.IsFalse(window.IsVisible, "Plain F12 inside the window hides it.");

            LiveLogWindow.ToggleWindow();
            Assert.IsTrue(window.IsVisible);

            window.KeyPress(Key.F12, RawInputModifiers.Shift, PhysicalKey.F12, null);
            Assert.IsTrue(window.IsVisible, "A modified F12 is the host's chord and must not hide the window.");

            LiveLogWindow.ToggleWindow();
            Assert.IsFalse(window.IsVisible, "The host's toggle still hides it.");
        }, CancellationToken.None);
    }
}

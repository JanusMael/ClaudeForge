using Avalonia;
using Avalonia.Controls;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Behaviors;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.Behaviors;

/// <summary>
/// Unit tests for the <see cref="LayeredEditors.Avalonia.Behaviors.FocusOnRequest"/>
/// attached property's shape.  The actual focus-on-bump effect requires
/// keyboard-input plumbing only available in a headless Avalonia harness,
/// so it's covered by manual smoke (Ctrl+F focuses the search box) +
/// the MainWindow.axaml binding contract test below.
///
/// These tests pin:
/// <list type="bullet">
///   <item>The attached property is registered as <see cref="int"/> on
///   <see cref="Control"/> with default <c>0</c>.</item>
///   <item>Get / Set round-trip on a real <see cref="Control"/>.</item>
///   <item>The property name is exactly <c>"RequestId"</c> — load-bearing
///   for the AXAML binding <c>behaviors:FocusOnRequest.RequestId="{...}"</c>.</item>
/// </list>
/// </summary>
[TestClass]
public sealed class FocusOnRequestTests
{
    [TestMethod]
    public void RequestIdProperty_IsRegisteredAsInt32()
    {
        AttachedProperty<int> prop = FocusOnRequest.RequestIdProperty;
        Assert.IsNotNull(prop, "RequestIdProperty must be a registered AttachedProperty.");
        Assert.AreEqual(typeof(int), prop.PropertyType,
            "Counter pattern requires int — bool would not fire change events on " +
            "successive bumps when the prior value happens to equal the new value.");
    }

    [TestMethod]
    public void RequestIdProperty_HasExpectedName()
    {
        // The AXAML binding `behaviors:FocusOnRequest.RequestId="{Binding ...}"`
        // depends on this exact property name.  A rename would compile but
        // silently break every consumer at runtime — this test catches that.
        Assert.AreEqual("RequestId", FocusOnRequest.RequestIdProperty.Name);
    }

    [TestMethod]
    public void GetSetRoundTrip_OnTextBox_PreservesValue()
    {
        TextBox tb = new();
        Assert.AreEqual(0, FocusOnRequest.GetRequestId(tb),
            "Default value of the attached int must be 0 so the initial " +
            "binding resolution doesn't appear to be a bump.");

        FocusOnRequest.SetRequestId(tb, 1);
        Assert.AreEqual(1, FocusOnRequest.GetRequestId(tb));

        FocusOnRequest.SetRequestId(tb, 42);
        Assert.AreEqual(42, FocusOnRequest.GetRequestId(tb));
    }

    [TestMethod]
    public void GetSetRoundTrip_OnGenericControl_Works()
    {
        // The attached property is registered on Control (not TextBox-specific)
        // so it can attach to any focusable control — Buttons, ListBoxes, etc.
        // A future consumer that wants to focus a Button on a VM bump should
        // Just Work.
        Button btn = new();
        FocusOnRequest.SetRequestId(btn, 7);
        Assert.AreEqual(7, FocusOnRequest.GetRequestId(btn));
    }
}
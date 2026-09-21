using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Converters;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.Helpers;

/// <summary>
/// A themed brush follows the theme variant after it has been handed out.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>The defect this pins was user-visible and nothing in the suite could see it.</b>
/// <c>BrushHelper.ResolveThemed</c> read <c>ActualThemeVariant</c> at Convert time and returned
/// that variant's brush. An <see cref="global::Avalonia.Data.Converters.IValueConverter"/> only
/// re-runs when its binding SOURCE changes, and a severity does not change because the theme did
/// — so each element kept whichever palette was live when it was last materialised. Rows rebuilt
/// by navigation picked up the new one, rows that were not kept the old one, and a single screen
/// showed both. Reported 2026-09-14 as "brighter on reopen, dark after switching to light, and
/// sometimes light then later dark inside the same theme".
/// </para>
/// <para>
/// ⭐ <b>The assertion is on the SAME brush instance, deliberately.</b> Asserting that a fresh
/// <c>Convert</c> returns the right colour would pass against the old snapshotting code too — a
/// new conversion always read the current variant. What was broken is the brush already held by
/// an element that nothing re-converted, so that is what this holds on to and re-reads.
/// </para>
/// </remarks>
[TestClass]
public sealed class ThemedBrushTrackingTests
{
    private const string Key = "AppSeverityCriticalBrush";

    private static readonly Color LightValue = Color.Parse("#CE2029");
    private static readonly Color DarkValue = Color.Parse("#F99090");

    private static HeadlessUnitTestSession Session =>
        HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

    [TestMethod]
    public Task AThemedBrushFollowsTheVariantAfterItHasBeenHandedOut()
    {
        return Session.Dispatch(() =>
        {
            Application app = Application.Current
                              ?? throw new InvalidOperationException("No Application in the headless session.");

            ThemeVariant original = app.RequestedThemeVariant ?? ThemeVariant.Default;
            ResourceDictionary themed = BuildThemedDictionary();
            app.Resources.MergedDictionaries.Add(themed);
            BrushHelper_ResetForTesting();

            try
            {
                app.RequestedThemeVariant = ThemeVariant.Light;

                AppSeverityToBrushConverter converter = new();
                object result = converter.Convert(
                    AppSeverity.Critical, typeof(IBrush), null, CultureInfo.InvariantCulture);

                // The premise: without this the test would be asserting against the fallback hex
                // rather than the dictionary, and would pass no matter what the theme did.
                Assert.IsInstanceOfType<ISolidColorBrush>(result,
                    "Expected a solid colour brush from the themed dictionary. If this fails the "
                    + "key was not found and every assertion below is measuring the fallback.");

                ISolidColorBrush handedOut = (ISolidColorBrush)result;
                Assert.AreEqual(LightValue, handedOut.Color, "Light variant colour");

                // Flip the variant WITHOUT re-running the converter, which is exactly the
                // situation an already-rendered row is in.
                app.RequestedThemeVariant = ThemeVariant.Dark;

                Assert.AreEqual(DarkValue, handedOut.Color,
                    "The brush an element is already holding did not follow the theme. It is a "
                    + "snapshot again, so a row that is not rebuilt will keep the previous "
                    + "palette and one screen can show both at once.");
            }
            finally
            {
                app.Resources.MergedDictionaries.Remove(themed);
                app.RequestedThemeVariant = original;
                BrushHelper_ResetForTesting();
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// The same two keys the apps declare, so the converter's themed lookup has something to
    /// find. The headless app is deliberately stripped of the real App resource dictionaries.
    /// </summary>
    private static ResourceDictionary BuildThemedDictionary()
    {
        ResourceDictionary light = new() { [Key] = new SolidColorBrush(LightValue) };
        ResourceDictionary dark = new() { [Key] = new SolidColorBrush(DarkValue) };

        ResourceDictionary root = new();
        root.ThemeDictionaries[ThemeVariant.Light] = light;
        root.ThemeDictionaries[ThemeVariant.Dark] = dark;
        return root;
    }

    /// <summary>
    /// <c>BrushHelper</c> is internal to the library and its cache is process-wide, so it has to
    /// be cleared around this test or a brush cached by an earlier test under another variant
    /// would be handed back here.
    /// </summary>
    private static void BrushHelper_ResetForTesting()
    {
        // ⚠ Deliberately tolerant of the method being ABSENT. This scaffolding must never be
        // what fails: the first canary of this test stashed the fix, and the test went red on a
        // NullReferenceException here rather than on its own assertion - a red that proved
        // nothing. A cache that cannot be cleared is a weaker test, not an invalid one.
        Type? helper = typeof(AppSeverityToBrushConverter).Assembly
            .GetType("Bennewitz.Ninja.LayeredEditors.Avalonia.Helpers.BrushHelper");

        helper?.GetMethod("ResetForTesting", BindingFlags.NonPublic | BindingFlags.Static)
              ?.Invoke(null, null);
    }
}

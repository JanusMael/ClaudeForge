using System.Reflection;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Localization;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.Themes;

/// <summary>
/// Guards the screen-reader names of the spinner buttons a <see cref="NumericUpDown"/>
/// draws — <c>Themes/AccessibilityNames.axaml</c>.
/// <para>
/// Why this test cannot be a markup scan. ClaudeForge's
/// <c>AxamlAccessibilityCoverageTests</c> reads <c>Views/*.axaml</c> and flags any
/// interactive control without <c>AutomationProperties.Name</c>. That scan is blind to
/// this defect by construction: the two buttons exist only in
/// <see cref="ButtonSpinner"/>'s control template, so there is no element in any view for
/// a scanner to find, and the <c>NumericUpDown</c> that IS in the view is correctly named
/// and would read as clean. The buttons have to be built for real and asked what they
/// would announce, which is what happens below.
/// </para>
/// <para>
/// The regression this catches: with no name set, Avalonia's
/// <c>ContentControlAutomationPeer</c> falls back to <c>Content?.ToString()</c>, and a
/// spinner button's content is a <c>PathIcon</c> — so a screen reader announces
/// "Avalonia.Controls.PathIcon". Measured through UIA on 2026-09-07: four such buttons on
/// ClaudeForge's Essentials page, six on OpenCodeForge's.
/// </para>
/// </summary>
[TestClass]
public sealed class SpinnerAutomationNameTests
{
    private static HeadlessUnitTestSession Session =>
        HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

    [TestMethod]
    public Task NumericUpDown_SpinnerButtons_AnnounceSomethingOtherThanTheirTypeName()
    {
        return Session.Dispatch(() =>
        {
            using SpinnerUnderTest subject = new();

            AssertAnnounces(subject.Increase, WrapperStrings.LabelSpinnerIncrease, "increase");
            AssertAnnounces(subject.Decrease, WrapperStrings.LabelSpinnerDecrease, "decrease");
        }, CancellationToken.None);
    }

    /// <summary>
    /// The two directions must not resolve to one string. Kept separate from the test
    /// above because it asserts about the string surface rather than about the buttons:
    /// an up button and a down button that announce identically are no more usable than
    /// two unnamed ones, and the shortest path to that is a copy-paste in
    /// <c>WrapperStrings</c> or in a host's resolver.
    /// </summary>
    [TestMethod]
    public void SpinnerDirections_DoNotShareOneString()
    {
        Assert.AreNotEqual(
            WrapperStrings.LabelSpinnerIncrease,
            WrapperStrings.LabelSpinnerDecrease,
            "Both spinner directions resolve to the same text, so a screen-reader user " +
            "cannot tell the up button from the down one.");
    }

    // ── Assertions ───────────────────────────────────────────────────────────

    private static void AssertAnnounces(RepeatButton button, string expected, string direction)
    {
        // What UIA actually reads. Going through the peer rather than reading the
        // attached property directly is the point of the test: the type-name fallback
        // lives in the peer, so only the peer can show it is gone.
        string? announced = PeerName(button);

        Assert.IsFalse(string.IsNullOrWhiteSpace(announced),
            $"The {direction} button of a NumericUpDown announces nothing. Themes/" +
            "AccessibilityNames.axaml is what names it; check that SemiBundle.axaml still " +
            "includes that file and that the /template/ selector still matches.");

        string? contentTypeName = button.Content?.GetType().FullName;
        Assert.AreNotEqual(contentTypeName, announced,
            $"The {direction} button of a NumericUpDown announces '{announced}' — the " +
            "ToString() of its own content. That is Avalonia's ContentControlAutomationPeer " +
            "fallback firing because nothing set AutomationProperties.Name, which is the exact " +
            "defect Themes/AccessibilityNames.axaml exists to fix.");

        Assert.IsFalse(announced!.StartsWith("Avalonia.", StringComparison.Ordinal),
            $"The {direction} button of a NumericUpDown announces '{announced}', which is a " +
            "type name rather than words. See Themes/AccessibilityNames.axaml.");

        // Pins the mechanism, not just the outcome: the text came from this library's
        // localisable string surface, so a host that wires WrapperStrings.Resolver
        // translates it along with the rest of the chrome.
        //
        // This compares a value read NOW against one the style baked in when the theme
        // loaded — {x:Static} dereferences once, at parse time. Nothing in this assembly
        // reassigns WrapperStrings.Resolver; a test that starts doing so has to restore it
        // (WrapperStrings.ResetForTesting) or this comparison turns into a false failure.
        Assert.AreEqual(expected, announced,
            $"The {direction} button announces '{announced}' rather than the " +
            $"WrapperStrings value '{expected}'. Something other than " +
            "Themes/AccessibilityNames.axaml is naming it, so the name will not localise.");
    }

    private static string? PeerName(Control control)
    {
        return ControlAutomationPeer.CreatePeerForElement(control).GetName();
    }

    // ── The subject ──────────────────────────────────────────────────────────

    /// <summary>
    /// A shown <see cref="Window"/> hosting one templated <see cref="NumericUpDown"/>,
    /// with its two spinner buttons located.
    /// <para>
    /// Every step asserts its own premise. A template whose parts get renamed, or one that
    /// stops drawing a <see cref="ButtonSpinner"/> at all, must fail loudly here rather
    /// than hand the test above an empty set to pass over — the failure mode where a
    /// green run means "found nothing to check".
    /// </para>
    /// </summary>
    private sealed class SpinnerUnderTest : IDisposable
    {
        private readonly Window _window;

        internal SpinnerUnderTest()
        {
            NumericUpDown numeric = new() { Value = 1, Width = 200 };
            _window = new Window { Width = 400, Height = 200, Content = numeric };
            _window.Show();

            // A premise assertion below throws before the caller's `using` binds, so the
            // window would stay open on the shared headless session and follow the failure
            // into every later test in this assembly. Close it here instead.
            try
            {
                List<ButtonSpinner> spinners = numeric.GetVisualDescendants()
                                                      .OfType<ButtonSpinner>()
                                                      .ToList();
                Assert.AreEqual(1, spinners.Count,
                    $"Expected a templated NumericUpDown to contain exactly one ButtonSpinner, " +
                    $"found {spinners.Count}. Either the theme bundle did not load (HeadlessTestApp) " +
                    "or the NumericUpDown template no longer draws a ButtonSpinner — in which case " +
                    "this whole test is looking at the wrong control and its result means nothing.");

                Increase = FindPart(spinners[0], "PART_IncreaseButton");
                Decrease = FindPart(spinners[0], "PART_DecreaseButton");
            }
            catch
            {
                _window.Close();
                throw;
            }
        }

        internal RepeatButton Increase { get; }

        internal RepeatButton Decrease { get; }

        public void Dispose()
        {
            _window.Close();
        }

        private static RepeatButton FindPart(ButtonSpinner spinner, string partName)
        {
            List<RepeatButton> matches = spinner.GetVisualDescendants()
                                                .OfType<RepeatButton>()
                                                .Where(b => string.Equals(b.Name, partName, StringComparison.Ordinal))
                                                .ToList();

            Assert.AreEqual(1, matches.Count,
                $"Expected exactly one RepeatButton named '{partName}' in the ButtonSpinner " +
                $"template, found {matches.Count}. The theme renamed or dropped the part, which " +
                "means the selector in Themes/AccessibilityNames.axaml no longer matches it " +
                "either — the buttons are back to announcing their type name. Update both.");

            return matches[0];
        }
    }
}

using System.Reflection;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Localization;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.Themes;

/// <summary>
/// Guards <c>Themes/AccessibilityNames.axaml</c> — the screen-reader names of control-template
/// parts, for the two kinds of gap measured through UIA on 2026-09-07.
/// <list type="number">
///   <item>
///   An interactive part with NO name. A <see cref="NumericUpDown"/>'s spinner buttons are
///   <c>RepeatButton</c>s whose content is a <c>PathIcon</c>, and Avalonia's
///   <c>ContentControlAutomationPeer</c> falls back to <c>Content?.ToString()</c> — so a screen
///   reader announced "Avalonia.Controls.PathIcon". Twelve such buttons across ClaudeForge, six
///   on OpenCodeForge's Essentials page alone.
///   </item>
///   <item>
///   A named control whose name is on the wrong element. <see cref="NumericUpDown"/> and
///   <see cref="AutoCompleteBox"/> hand focus to an inner <c>PART_TextBox</c> that had no name of
///   its own, so the name the view set sat on an element that never holds focus
///   (<c>HasKeyboardFocus</c> read True on the inner Edit, False on the named Spinner).
///   </item>
/// </list>
/// <para>
/// Why none of this can be a markup scan. ClaudeForge's
/// <c>AxamlAccessibilityCoverageTests</c> reads <c>Views/*.axaml</c> and flags any interactive
/// control without <c>AutomationProperties.Name</c>. It is blind to both gaps by construction:
/// the parts exist only inside control templates, so there is no element in any view to scan,
/// and the control that IS in the view reads as clean either way. The parts have to be built for
/// real and asked what they would announce, which is what happens below.
/// </para>
/// </summary>
[TestClass]
public sealed class TemplatePartAutomationNameTests
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
    /// The element that takes keyboard focus inside a <see cref="NumericUpDown"/> is its
    /// inner <c>PART_TextBox</c>, not the correctly-named control the view declared — so
    /// the name has to reach the text box as well, or a screen-reader user lands on an
    /// unnamed edit field. Measured through UIA on 2026-09-07: <c>HasKeyboardFocus</c> read
    /// True on the inner Edit and False on the Spinner above it.
    /// </summary>
    [TestMethod]
    public Task NumericUpDown_FocusTarget_InheritsTheNameTheViewSet()
    {
        return Session.Dispatch(() =>
        {
            using SpinnerUnderTest subject = new();

            Assert.IsTrue(subject.Text.Focusable,
                "The inner PART_TextBox is not focusable, so this test is no longer about " +
                "the element that takes focus. Re-measure where focus lands before trusting it.");

            Assert.AreEqual(SpinnerUnderTest.HostName, PeerName(subject.Text),
                $"A NumericUpDown named '{SpinnerUnderTest.HostName}' has an inner PART_TextBox " +
                $"that announces '{PeerName(subject.Text)}'. That text box is what takes focus, " +
                "so the view's name has to reach it — see the inherited-names section of " +
                "Themes/AccessibilityNames.axaml.");
        }, CancellationToken.None);
    }

    /// <summary>
    /// Same defect, same fix, different control: an <see cref="AutoCompleteBox"/> surfaces
    /// as a named <c>ControlType.Group</c> and hands focus to its own inner
    /// <c>PART_TextBox</c>. Eight of them across ClaudeForge, including the Essentials
    /// model picker.
    /// </summary>
    [TestMethod]
    public Task AutoCompleteBox_FocusTarget_InheritsTheNameTheViewSet()
    {
        return Session.Dispatch(() =>
        {
            using AutoCompleteUnderTest subject = new();

            Assert.AreEqual(AutoCompleteUnderTest.HostName, PeerName(subject.Text),
                $"An AutoCompleteBox named '{AutoCompleteUnderTest.HostName}' has an inner " +
                $"PART_TextBox that announces '{PeerName(subject.Text)}'. See the " +
                "inherited-names section of Themes/AccessibilityNames.axaml.");
        }, CancellationToken.None);
    }

    /// <summary>
    /// Copying the host's name down must not invent one. A control whose view left
    /// <c>AutomationProperties.Name</c> unset has to stay unnamed, so the AXAML coverage
    /// guard still sees the gap instead of this style papering over it.
    /// </summary>
    [TestMethod]
    public Task FocusTarget_OfAnUnnamedHost_StaysUnnamed()
    {
        return Session.Dispatch(() =>
        {
            using SpinnerUnderTest subject = new(nameTheHost: false);

            Assert.IsTrue(string.IsNullOrEmpty(PeerName(subject.Text)),
                $"A NumericUpDown with no AutomationProperties.Name produced an inner text box " +
                $"announcing '{PeerName(subject.Text)}'. The inherited-name style must copy the " +
                "host's name and nothing else, or it hides a missing name from the AXAML guard.");
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
        /// <summary>Stands in for the card title a view binds, e.g. "Max Output Tokens".</summary>
        internal const string HostName = "Host control name";

        private readonly Window _window;

        internal SpinnerUnderTest(bool nameTheHost = true)
        {
            NumericUpDown numeric = new() { Value = 1, Width = 200 };
            if (nameTheHost)
            {
                AutomationProperties.SetName(numeric, HostName);
            }

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

                Increase = FindRepeatButton(spinners[0], "PART_IncreaseButton");
                Decrease = FindRepeatButton(spinners[0], "PART_DecreaseButton");

                // Scoped to the NumericUpDown, not to the ButtonSpinner: PART_TextBox renders
                // inside the spinner but belongs to the NumericUpDown's own template, and the
                // style selector has to agree with that. FindTextBoxPart asserts it.
                Text = FindTextBoxPart(numeric);
            }
            catch
            {
                _window.Close();
                throw;
            }
        }

        internal RepeatButton Increase { get; }

        internal RepeatButton Decrease { get; }

        internal TextBox Text { get; }

        public void Dispose()
        {
            _window.Close();
        }

        private static RepeatButton FindRepeatButton(ButtonSpinner spinner, string partName)
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

    /// <summary>
    /// A shown <see cref="Window"/> hosting one templated <see cref="AutoCompleteBox"/>, with
    /// its inner text box located. Same premise discipline as <see cref="SpinnerUnderTest"/>.
    /// </summary>
    private sealed class AutoCompleteUnderTest : IDisposable
    {
        internal const string HostName = "Picker control name";

        private readonly Window _window;

        internal AutoCompleteUnderTest()
        {
            AutoCompleteBox box = new() { Width = 200 };
            AutomationProperties.SetName(box, HostName);
            _window = new Window { Width = 400, Height = 200, Content = box };
            _window.Show();

            try
            {
                Text = FindTextBoxPart(box);
            }
            catch
            {
                _window.Close();
                throw;
            }
        }

        internal TextBox Text { get; }

        public void Dispose()
        {
            _window.Close();
        }
    }

    /// <summary>
    /// The single <c>PART_TextBox</c> whose <c>TemplatedParent</c> is
    /// <paramref name="host"/> — the element a style scoped to
    /// <c>&lt;host&gt; /template/ TextBox#PART_TextBox</c> would match.
    /// <para>
    /// Asserting on <c>TemplatedParent</c> rather than just the name is what keeps the
    /// selector and the test from drifting apart: a part that moved into a nested control's
    /// template would still be findable by name, and the style would silently stop matching
    /// it while a name-only search kept passing.
    /// </para>
    /// </summary>
    private static TextBox FindTextBoxPart(Control host)
    {
        List<TextBox> matches = host.GetVisualDescendants()
                                    .OfType<TextBox>()
                                    .Where(t => string.Equals(t.Name, "PART_TextBox", StringComparison.Ordinal)
                                                && ReferenceEquals(t.TemplatedParent, host))
                                    .ToList();

        Assert.AreEqual(1, matches.Count,
            $"Expected exactly one TextBox named 'PART_TextBox' templated by " +
            $"{host.GetType().Name}, found {matches.Count}. Either the template renamed the part " +
            "or it now belongs to a nested control's template — either way the selector in " +
            "Themes/AccessibilityNames.axaml no longer matches the element that takes focus, " +
            "and it has to be re-scoped to whatever the new TemplatedParent is.");

        return matches[0];
    }
}

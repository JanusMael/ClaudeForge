using System.Reflection;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Localization;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.Themes;

/// <summary>
/// Guards the <c>TreeViewItem</c> expand/collapse chevron's screen-reader name — finding
/// <c>F6</c>, where <c>scripts/Audit-Accessibility.ps1</c> found 26 unnamed chevrons in the
/// navigation tree, each reaching a reader as a bare "button".
/// </summary>
/// <remarks>
/// <para>
/// ⛔ <b>Only a runtime test can see this.</b> The chevron is supplied by the consumed theme's
/// <c>TreeViewItem</c> template, so no view declares it, nothing in markup can carry an
/// <c>AutomationProperties.Name</c> for it, and the repo-wide AXAML accessibility scan has no
/// element to flag. The name is set by a style in <c>Themes/AccessibilityNames.axaml</c>, and only
/// the automation peer can show that it arrived.
/// </para>
/// <para>
/// ⭐ <b><see cref="ThePremiseHolds_TheChevronIsTheOnlyExpandCollapseAffordance"/> pins the
/// REASONING, not just the behaviour.</b> The obvious alternative fix was to hide the chevron with
/// <c>AccessibilityView="Raw"</c>, on the stated grounds that the <c>TreeViewItem</c> already
/// exposes an ExpandCollapse pattern and the chevron merely duplicates it. Measured, that is false
/// on this Avalonia: <c>TreeViewItemAutomationPeer</c> offers <c>IScrollProvider</c> and
/// <c>ISelectionItemProvider</c> only, while the chevron's <c>ToggleButtonAutomationPeer</c>
/// carries <c>IToggleProvider</c> — the ONLY programmatic expand/collapse affordance in the tree.
/// Hiding it would have removed the affordance rather than a duplicate. If a later Avalonia adds
/// ExpandCollapse to the item, that test reddens and the choice gets made again on the new facts
/// instead of being inherited.
/// </para>
/// <para>
/// ⚠ <b>The part is a <see cref="ToggleButton"/>, though UIA reports its control type as
/// Button.</b> That is the peer's answer, not the element's, and a style selector written from a
/// UIA dump matches nothing and fails silently.
/// </para>
/// </remarks>
[TestClass]
public sealed class TreeChevronAutomationNameTests
{
    private const string ChevronPart = "PART_ExpandCollapseChevron";

    private static HeadlessUnitTestSession Session =>
        HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

    [TestMethod]
    public Task TheChevronAnnouncesItsName() => Session.Dispatch(() =>
    {
        using TreeUnderTest tree = new();

        string? name = ControlAutomationPeer.CreatePeerForElement(tree.Chevron).GetName();

        Assert.AreEqual(WrapperStrings.LabelExpandCollapse, name,
            $"the expand/collapse chevron announces '{name}'. A screen reader reaches one of these "
            + "per expandable node in the navigation tree — 26 of them when F6 was measured — and "
            + "with no name each is read out as a bare \"button\". The name comes from the "
            + "TreeViewItem /template/ ToggleButton#PART_ExpandCollapseChevron style in "
            + "Themes/AccessibilityNames.axaml; check the bundle include and the selector's "
            + "element TYPE, which is ToggleButton and not Button.");
    }, CancellationToken.None);

    /// <summary>
    /// The reason the chevron is named rather than hidden. Stated as an assertion so it can be
    /// refuted by a future Avalonia rather than quietly outliving its evidence.
    /// </summary>
    [TestMethod]
    public Task ThePremiseHolds_TheChevronIsTheOnlyExpandCollapseAffordance() => Session.Dispatch(() =>
    {
        using TreeUnderTest tree = new();

        AutomationPeer itemPeer = ControlAutomationPeer.CreatePeerForElement(tree.Item);
        AutomationPeer chevronPeer = ControlAutomationPeer.CreatePeerForElement(tree.Chevron);

        Assert.IsNull(itemPeer.GetProvider<IExpandCollapseProvider>(),
            "the TreeViewItem now exposes an ExpandCollapse pattern. That changes the F6 decision: "
            + "the chevron becomes a genuine duplicate and hiding it with "
            + "AutomationProperties.AccessibilityView=\"Raw\" becomes the better fix, because a "
            + "reader would otherwise announce two ways to do one thing. Re-decide rather than "
            + "deleting this assertion.");

        Assert.IsNotNull(chevronPeer.GetProvider<IToggleProvider>(),
            "the chevron no longer exposes a Toggle pattern, so nothing in the tree offers "
            + "expand/collapse to assistive technology and naming it is no longer sufficient");
    }, CancellationToken.None);

    /// <summary>
    /// A shown <see cref="Window"/> hosting one expandable <see cref="TreeViewItem"/>, with its
    /// chevron located. Every step asserts its own premise: a renamed or re-typed template part
    /// must fail loudly here rather than hand the tests above an empty set to pass over.
    /// </summary>
    private sealed class TreeUnderTest : IDisposable
    {
        private readonly Window _window;

        internal TreeUnderTest()
        {
            Item = new TreeViewItem { Header = "parent", IsExpanded = false };
            Item.Items.Add(new TreeViewItem { Header = "child" });

            TreeView tree = new();
            tree.Items.Add(Item);

            _window = new Window { Width = 400, Height = 300, Content = tree };
            _window.Show();

            // A premise assertion throws before the caller's `using` binds, so the window would
            // stay open on the shared headless session and follow the failure into every later
            // test in this assembly. Close it here instead.
            try
            {
                List<Control> parts = [.. tree.GetVisualDescendants()
                    .OfType<Control>()
                    .Where(c => c.Name == ChevronPart)];

                Assert.AreEqual(1, parts.Count,
                    $"expected exactly one {ChevronPart} under a templated TreeViewItem with "
                    + $"children, found {parts.Count}. Either the theme bundle did not load "
                    + "(HeadlessTestApp) or the TreeViewItem template renamed its part — in which "
                    + "case the style in AccessibilityNames.axaml no longer matches anything and "
                    + "these tests would be checking a control that is not the subject.");

                Assert.IsInstanceOfType<ToggleButton>(parts[0],
                    $"{ChevronPart} is a {parts[0].GetType().Name}, not a ToggleButton. The style "
                    + "selector names ToggleButton, so it now matches nothing and the name is "
                    + "silently absent.");

                Assert.AreSame(Item, parts[0].TemplatedParent,
                    $"{ChevronPart}'s TemplatedParent is "
                    + $"{parts[0].TemplatedParent?.GetType().Name ?? "null"}, not the TreeViewItem. "
                    + "A /template/ selector is scoped to the TEMPLATING control, which UIA never "
                    + "reports — it shows nesting only — so the selector's subject has to change "
                    + "with this.");

                Chevron = (ToggleButton)parts[0];
            }
            catch
            {
                _window.Close();
                throw;
            }
        }

        internal TreeViewItem Item { get; }

        internal ToggleButton Chevron { get; }

        public void Dispose() => _window.Close();
    }
}

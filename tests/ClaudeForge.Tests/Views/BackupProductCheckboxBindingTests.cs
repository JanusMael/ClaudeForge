using System.Xml.Linq;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Views;

/// <summary>
/// Binding contract for the Backup tab's per-product checkbox list in
/// <c>BackupRestoreView.axaml</c>.
/// </summary>
/// <remarks>
/// <para>
/// Phase 4d-3 replaced two fixed <c>CheckBox</c> elements — bound to
/// <c>IncludeClaudeCode</c> and <c>IncludeClaudeDesktop</c> — with an
/// <c>ItemsControl</c> over <c>SelectableProducts</c>. The view-model side is covered by
/// <c>BackupRestoreViewModelTests</c>; what those cannot see is the markup. A renamed
/// view-model member still compiles, and the binding then fails silently at runtime: the
/// list renders empty, and a user's product selection is quietly ignored.
/// </para>
/// <para>
/// This is the same idiom <c>AxamlAccessibilityCoverageTests</c> uses — parse the markup as
/// XML and assert on it — because the project has no harness that renders a real view. The
/// headless Avalonia app is deliberately stripped of the App's resource dictionaries, so
/// instantiating this view in a test is not currently possible; the visual result is
/// confirmed by running the app.
/// </para>
/// </remarks>
[TestClass]
public sealed class BackupProductCheckboxBindingTests
{
    private const string ViewFileName = "BackupRestoreView.axaml";

    [TestMethod]
    public void ProductCheckboxes_BindToTheViewModelMembersThatStillExist()
    {
        XDocument doc = XDocument.Load(Path.Combine(ViewsDirectory(), ViewFileName));

        XElement itemsControl = doc.Descendants()
                                   .Where(e => e.Name.LocalName == "ItemsControl")
                                   .SingleOrDefault(e =>
                                       (string?)e.Attribute("ItemsSource") == "{Binding SelectableProducts}")
                                ?? throw new AssertFailedException(
                                       "No ItemsControl bound to SelectableProducts. Either the products list "
                                       + "was renamed on the view-model without updating the markup, or the "
                                       + "per-product checkboxes went back to being hardcoded.");

        XElement checkBox = itemsControl.Descendants()
                                        .Single(e => e.Name.LocalName == "CheckBox");

        Assert.AreEqual("{Binding IsSelected}", (string?)checkBox.Attribute("IsChecked"),
            "The checkbox must two-way bind IsSelected, or toggling it changes nothing and "
            + "the backup silently covers products the user deselected.");
        Assert.AreEqual("{Binding DisplayName}", (string?)checkBox.Attribute("Content"),
            "The label comes from the item view-model, which resolves it from the resource "
            + "table — that is how the nine locale translations survived the move out of markup.");

        // Accessibility invariant I20: every interactive control announces itself. Inside a
        // template the name has to be bound, since there is no longer a static per-product
        // label in the markup to point at. Attached-property attributes carry a literal dot
        // in their LocalName — XDocument reads them unmangled in the default xmlns.
        Assert.AreEqual("{Binding DisplayName}",
            checkBox.Attributes()
                    .SingleOrDefault(a => a.Name.LocalName == "AutomationProperties.Name")?.Value,
            "AutomationProperties.Name must bind DisplayName so a screen reader announces "
            + "each product rather than an unnamed checkbox.");
    }

    [TestMethod]
    public void TheRetiredPerProductBindingsAreGone()
    {
        // Counter-direction: if the old bindings were left behind alongside the new list, the
        // page would show duplicated checkboxes and the dead ones would bind to view-model
        // members that no longer exist.
        //
        // Asserted over BINDING ATTRIBUTE VALUES, not the file's raw text. A text search also
        // matches the comment above the ItemsControl, which explains what the markup used to
        // bind to — so the first version of this test failed on prose describing the past.
        XDocument doc = XDocument.Load(Path.Combine(ViewsDirectory(), ViewFileName));

        List<string> retired = doc.Descendants()
                                  .SelectMany(e => e.Attributes())
                                  .Select(a => a.Value)
                                  .Where(v => v.Contains("IncludeClaudeCode", StringComparison.Ordinal)
                                              || v.Contains("IncludeClaudeDesktop", StringComparison.Ordinal))
                                  .ToList();

        Assert.AreEqual(0, retired.Count,
            "These view-model members no longer exist, so any binding to them is dead: "
            + string.Join(" | ", retired));
    }

    /// <summary>Locates <c>src/ClaudeForge/Views</c> by walking up from the test binary.</summary>
    private static string ViewsDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            string candidate = Path.Combine(dir, "src", "ClaudeForge", "Views");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            "Could not locate src/ClaudeForge/Views by walking up from "
            + $"AppContext.BaseDirectory = '{AppContext.BaseDirectory}'.");
    }
}

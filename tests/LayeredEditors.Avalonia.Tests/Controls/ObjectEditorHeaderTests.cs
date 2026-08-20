using System.Xml.Linq;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.Controls;

/// <summary>
/// The object editor's expander must not repeat the property name.
/// </summary>
/// <remarks>
/// <para>
/// The wrapper renders the property name once already, in its own label row. Binding
/// <c>DisplayName</c> to the expander header as well printed every nested object's name twice —
/// visible in the second app as a "watcher" label sitting directly above a "watcher" expander.
/// </para>
/// <para>
/// ⚠ Parsed as XML and asserted over ATTRIBUTE VALUES rather than searched as text, which is the
/// idiom this repo settled on after an earlier version of a similar test failed on a comment
/// describing what the markup used to bind to.
/// </para>
/// </remarks>
[TestClass]
public sealed class ObjectEditorHeaderTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    private static string WrapperPath()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            string candidate = Path.Combine(
                dir, "src", "LayeredEditors.Avalonia", "Controls", "PropertyEditorWrapper.axaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Could not locate PropertyEditorWrapper.axaml.");
    }

    private static XElement ObjectTemplateExpander()
    {
        XDocument doc = XDocument.Load(WrapperPath());

        XElement template = doc.Descendants(Avalonia + "DataTemplate")
            .Single(e => (string?)e.Attribute(
                XName.Get("DataType", "http://schemas.microsoft.com/winfx/2006/xaml"))
                == "vm:ObjectPropertyEditorViewModel");

        return template.Descendants(Avalonia + "Expander").First();
    }

    [TestMethod]
    public void ObjectExpanderHeader_DoesNotBindDisplayName()
    {
        string? header = (string?)ObjectTemplateExpander().Attribute("Header");

        Assert.IsNotNull(header, "The object template's expander should still declare a header.");
        Assert.IsFalse(
            header.Contains("DisplayName", StringComparison.Ordinal),
            "The expander header binds DisplayName, which the wrapper's label row already shows — "
            + "so every nested object renders its name twice. Bind something that adds "
            + $"information instead. Current header: '{header}'");
    }

    /// <summary>
    /// The label row itself must keep showing the name — removing the duplication by deleting the
    /// wrong one would leave nested objects unlabelled entirely.
    /// </summary>
    [TestMethod]
    public void TheLabelRow_StillShowsTheName()
    {
        XDocument doc = XDocument.Load(WrapperPath());

        bool labelled = doc.Descendants(Avalonia + "TextBlock").Any(e =>
            (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))
                == "PropertyNameLabel"
            && ((string?)e.Attribute("Text"))?.Contains("DisplayName", StringComparison.Ordinal) == true);

        Assert.IsTrue(labelled,
            "PropertyNameLabel no longer binds DisplayName. The duplication must be fixed by "
            + "changing the expander header, not by removing the only label a nested leaf has.");
    }
}

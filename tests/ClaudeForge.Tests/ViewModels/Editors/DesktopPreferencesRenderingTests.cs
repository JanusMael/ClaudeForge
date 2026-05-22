using System.Reflection;
using Bennewitz.Ninja.ClaudeForge.Adapters;
using Json.Schema;
using PropertyEditorViewModel = Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels.PropertyEditorViewModel;
using SchemaRegistry = Bennewitz.Ninja.ClaudeForge.Core.Schema.SchemaRegistry;
using SchemaValueType = Bennewitz.Ninja.ClaudeForge.Core.Schema.SchemaValueType;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// Regression tests for the Claude Desktop <c>preferences</c> object: every nested
/// boolean child must carry a non-empty <see cref="Bennewitz.Ninja.ClaudeForge.ViewModels.Editors.PropertyEditorViewModel.DisplayName"/>
/// so the XAML can render a labelled tri-state checkbox. The earlier Object DataTemplate
/// rendered children via a bare <c>ContentControl</c> which stripped the wrapper chrome,
/// leaving checkboxes without any visible label.
/// </summary>
[TestClass]
public class DesktopPreferencesRenderingTests
{
    private static SchemaNode LoadDesktopPreferencesNode()
    {
        // Load the bundled schema resource directly, bypassing SchemaRegistry's on-disk cache
        // so the test asserts against the repository schema rather than a stale user cache.
        Assembly assembly = typeof(SchemaRegistry).Assembly;
        const string resourceName = Core.ResourceHelper.ResourcePrefix + ".Core.Assets.Schemas.claude-desktop-config.json";

        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        Assert.IsNotNull(stream, $"Embedded resource '{resourceName}' must exist.");
        using StreamReader reader = new(stream!);
        string json = reader.ReadToEnd();

        BuildOptions opts = new() { SchemaRegistry = new Json.Schema.SchemaRegistry() };
        JsonSchemaNode root = JsonSchema.FromText(json, opts).Root!;
        IReadOnlyList<SchemaNode> top = SchemaTreeBuilder.BuildTopLevel(root);
        return top.First(n => n.Name == "preferences");
    }

    [TestMethod]
    public void Preferences_ChildrenHaveDisplayNames()
    {
        SchemaNode prefsNode = LoadDesktopPreferencesNode();

        Assert.AreEqual(SchemaValueType.Object, prefsNode.ValueType);
        Assert.IsTrue(prefsNode.Properties.Count > 0, "preferences must expose child properties.");

        foreach (SchemaNode child in prefsNode.Properties)
        {
            // The schema provides a "title" for every preference — this is what
            // PropertyEditorViewModel.DisplayName exposes and what the XAML binds to.
            // Missing titles are what caused unlabeled checkboxes.
            string label = child.Title ?? child.Name;
            Assert.IsFalse(string.IsNullOrWhiteSpace(label),
                $"Preference '{child.Name}' must have a non-empty title/name for rendering.");
        }
    }

    [TestMethod]
    public void Preferences_ObjectEditor_YieldsLabeledChildEditors()
    {
        SchemaNode prefsNode = LoadDesktopPreferencesNode();
        CompositeEditorFactory factory = ClaudeEditorFactoryConfig.CreateDefault();
        PropertyEditorViewModel editor = factory.Create(prefsNode, ConfigScope.User);

        Assert.IsInstanceOfType(editor, typeof(ObjectPropertyEditorViewModel));
        ObjectPropertyEditorViewModel obj = (ObjectPropertyEditorViewModel)editor;

        Assert.IsTrue(obj.Children.Count > 0, "Object editor must produce child editors.");
        foreach (PropertyEditorViewModel childEditor in obj.Children)
        {
            // The nested Object DataTemplate recursion requires each child editor to expose
            // its own DisplayName — that's the label the recursive PropertyEditorWrapper
            // renders above the leaf control.
            Assert.IsFalse(string.IsNullOrWhiteSpace(childEditor.DisplayName),
                $"Child editor for '{childEditor.Schema.Name}' must have a DisplayName.");
        }
    }
}
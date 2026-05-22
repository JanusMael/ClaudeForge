using Bennewitz.Ninja.ClaudeForge.ViewModels;
using PropertyEditorViewModel = Bennewitz.Ninja.LayeredEditors.Avalonia.ViewModels.PropertyEditorViewModel;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// deprecated properties must be hidden from
/// <see cref="SettingsGroupEditorViewModel.FilteredEditors"/> unless a value is
/// already set at some scope (so the user can still unset it).
/// </summary>
[TestClass]
public sealed class DeprecatedFilterTests
{
    private static SettingsWorkspace MakeWorkspace(params (ConfigScope Scope, string Json)[] entries)
    {
        IEnumerable<SettingsDocument> docs = entries.Select(e =>
        {
            JsonObject root = (JsonObject)JsonNode.Parse(e.Json)!;
            return new SettingsDocument(e.Scope, $"{e.Scope}.json", root, isReadOnly: false);
        });
        return new SettingsWorkspace(docs);
    }

    [TestMethod]
    public void DeprecatedUnset_IsHiddenFromFilteredEditors()
    {
        List<SchemaNode> nodes =
        [
            new("includeCoAuthoredBy", "includeCoAuthoredBy")
            {
                ValueType = SchemaValueType.Boolean,
                IsDeprecated = true,
            },

            new("model", "model") { ValueType = SchemaValueType.String },

        ];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));

        SettingsGroupEditorViewModel vm = new("Git", nodes, workspace);

        List<PropertyEditorViewModel> filtered = vm.FilteredEditors.ToList();
        Assert.AreEqual(1, filtered.Count,
            "Deprecated+unset property must not appear in the filtered list.");
        Assert.AreEqual("model", filtered[0].Path);
    }

    [TestMethod]
    public void DeprecatedSet_IsVisibleSoUserCanUnsetIt()
    {
        List<SchemaNode> nodes =
        [
            new("includeCoAuthoredBy", "includeCoAuthoredBy")
            {
                ValueType = SchemaValueType.Boolean,
                IsDeprecated = true,
            },

        ];
        SettingsWorkspace workspace = MakeWorkspace(
            (ConfigScope.User, """{"includeCoAuthoredBy":true}"""));

        SettingsGroupEditorViewModel vm = new("Git", nodes, workspace);

        List<PropertyEditorViewModel> filtered = vm.FilteredEditors.ToList();
        Assert.AreEqual(1, filtered.Count,
            "Deprecated property that is set at a scope must still be visible so the user can remove it.");
        Assert.AreEqual("includeCoAuthoredBy", filtered[0].Path);
    }

    [TestMethod]
    public void NonDeprecated_IsAlwaysVisible()
    {
        List<SchemaNode> nodes =
        [
            new("model", "model") { ValueType = SchemaValueType.String },
        ];
        SettingsWorkspace workspace = MakeWorkspace((ConfigScope.User, "{}"));

        SettingsGroupEditorViewModel vm = new("Models", nodes, workspace);

        Assert.AreEqual(1, vm.FilteredEditors.Count());
    }
}
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Sdk;
using Bennewitz.Ninja.ClaudeForge.Sdk.Claude;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels.Editors;

/// <summary>
/// Pins the model/scope ↔ permissions.defaultMode gating. <c>auto</c> is dropped
/// as an offerable choice when ineligible (non-auto model OR non-User scope), but
/// the gating is <b>advisory-only and never rewrites the persisted value</b>:
/// loading a doc with <c>defaultMode:"auto"</c> at a non-User scope keeps it
/// "auto" (an unrelated later edit must not clobber it to "default").
/// </summary>
public sealed class PermissionsAutoModeGatingTests
{
    private static (PermissionsEditorViewModel vm, ClaudeCodeClient client) Open(
        ConfigScope scope, string model, string defaultMode)
    {
        JsonObject root = new()
        {
            ["model"] = model,
            ["permissions"] = new JsonObject { ["defaultMode"] = defaultMode },
        };
        SettingsDocument doc = new(scope, "settings.json", root, isReadOnly: false);
        SettingsWorkspace ws = new([doc], ClaudeMergePolicy.Instance);
        ClaudeCodeClient client = ClaudeCodeClient.FromExistingWorkspace(ClaudeEnvironment.Empty, 
            ws, scope, new SchemaRegistry());

        PermissionsEditorViewModel vm = new(
            new SchemaNode("permissions", "permissions") { ValueType = SchemaValueType.Complex },
            scope,
            client);

        JsonNode perms = root["permissions"]!;
        LayeredValue layered = new("permissions", [new ScopeEntry(scope, perms, "/fake")])
        {
            EffectiveValue = perms,
            EffectiveScope = scope,
        };
        vm.LoadFromLayered(layered, scope);
        return (vm, client);
    }

    private static string? EmittedDefaultMode(PermissionsEditorViewModel vm)
        => (vm.ToJsonValue() as JsonObject)?["defaultMode"]?.GetValue<string>();

    private static List<string> ModeValues(PermissionsEditorViewModel vm)
        => vm.DefaultModeInfos.Select(i => i.Value).ToList();

    [Fact]
    public void Auto_OnAutoCapableModel_AtUserScope_IsKeptAndOffered()
    {
        (PermissionsEditorViewModel vm, ClaudeCodeClient client) = Open(ConfigScope.User, "claude-opus-4-8", "auto");
        using (client)
        {
            Assert.Equal("auto", vm.DefaultMode);
            Assert.False(vm.ShowAutoModeWarning);
            MessageAssert.Contains("auto", ModeValues(vm), "auto is offered for an auto-capable model at User scope.");
        }
    }

    [Fact]
    public void Auto_OnNonAutoModel_IsAdvised_NotCoerced()
    {
        (PermissionsEditorViewModel vm, ClaudeCodeClient client) = Open(ConfigScope.User, "claude-haiku-4-5", "auto");
        using (client)
        {
            MessageAssert.Equal("auto", vm.DefaultMode, "Persisted auto is preserved, not coerced.");
            Assert.True(vm.ShowAutoModeWarning, "An advisory explains it won't take effect.");
            MessageAssert.Equal("auto", EmittedDefaultMode(vm), "Save round-trips the original value.");
            MessageAssert.Contains("auto", ModeValues(vm), "The current value stays visible (not blank).");
        }
    }

    [Fact]
    public void Auto_AtNonUserScope_IsAdvised_NotClobbered()
    {
        // The high-severity regression: opus IS auto-capable, but auto is userScopeOnly,
        // so at Project scope it's ineligible. It must NOT be silently rewritten to "default".
        (PermissionsEditorViewModel vm, ClaudeCodeClient client) = Open(ConfigScope.Project, "claude-opus-4-8", "auto");
        using (client)
        {
            MessageAssert.Equal("auto", vm.DefaultMode, "auto at Project scope is preserved.");
            Assert.True(vm.ShowAutoModeWarning);
            MessageAssert.Equal("auto", EmittedDefaultMode(vm),
                "An unrelated later edit live-writes ToJsonValue — it must still carry 'auto', not a coerced 'default'.");
        }
    }

    [Fact]
    public void Auto_IsNotOfferedAsAFreshChoice_WhenIneligible()
    {
        // Current mode is 'default' (not auto), so auto is not re-added — it's simply filtered out.
        (PermissionsEditorViewModel vm, ClaudeCodeClient client) = Open(ConfigScope.Project, "claude-opus-4-8", "default");
        using (client)
        {
            MessageAssert.DoesNotContain("auto", ModeValues(vm),
                "auto is removed from the offerable choices at a non-User scope.");
            Assert.False(vm.ShowAutoModeWarning, "No advisory when the current mode is eligible.");
        }
    }

    [Fact]
    public void NonAutoMode_IsUntouched()
    {
        (PermissionsEditorViewModel vm, ClaudeCodeClient client) = Open(ConfigScope.Project, "claude-haiku-4-5", "acceptEdits");
        using (client)
        {
            MessageAssert.Equal("acceptEdits", vm.DefaultMode, "Only auto is gated; other modes are unaffected.");
            Assert.False(vm.ShowAutoModeWarning);
        }
    }
}

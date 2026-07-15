using Bennewitz.Ninja.ClaudeForge.Avalonia.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions;
using Bennewitz.Ninja.ClaudeForge.Sdk.Permissions.Matching;

namespace Bennewitz.Ninja.ClaudeForge.Avalonia.Tests.Permissions;

/// <summary>Captures rules added by the builder. Optionally returns a canned
/// collision so the builder's collision-note path can be exercised.</summary>
internal sealed class FakeSink : IPermissionRuleSink
{
    public List<PermissionRule> Allow { get; } = [];
    public List<PermissionRule> Deny { get; } = [];
    public List<PermissionRule> Ask { get; } = [];

    /// <summary>When set, every Add returns this collision.</summary>
    public PermissionCollision? NextCollision { get; set; }

    public PermissionCollision? AddAllow(PermissionRule rule)
    {
        Allow.Add(rule);
        return NextCollision;
    }

    public PermissionCollision? AddDeny(PermissionRule rule)
    {
        Deny.Add(rule);
        return NextCollision;
    }

    public PermissionCollision? AddAsk(PermissionRule rule)
    {
        Ask.Add(rule);
        return NextCollision;
    }
}

/// <summary>Returns canned, test-controlled rule lists.</summary>
internal sealed class FakeSource : IPermissionRuleSource
{
    public ConfigScope EditingScope { get; set; } = ConfigScope.User;
    public PermissionDefaultMode? DefaultMode { get; set; } = PermissionDefaultMode.Default;
    public ScopedPermissionRules Editing { get; set; } =
        new(ConfigScope.User, [], [], []);
    public List<ScopedPermissionRules> All { get; } = [];

    public ScopedPermissionRules GetEditingScopeRules() => Editing;
    public IReadOnlyList<ScopedPermissionRules> GetAllScopeRules() => All;

    public static ScopedPermissionRules Scope(
        ConfigScope scope, string[]? allow = null, string[]? deny = null, string[]? ask = null) =>
        new(
            scope,
            (allow ?? []).Select(PermissionRule.Parse).ToList(),
            (deny ?? []).Select(PermissionRule.Parse).ToList(),
            (ask ?? []).Select(PermissionRule.Parse).ToList());
}

/// <summary>Returns a fixed path, simulating a user selection.</summary>
internal sealed class FakePathPicker(string? file = null, string? folder = null) : IPermissionPathPicker
{
    public Task<string?> PickFileAsync() => Task.FromResult(file);
    public Task<string?> PickFolderAsync() => Task.FromResult(folder);
}

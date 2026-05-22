using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Adapters;

/// <summary>
/// Wraps a <see cref="LayeredValue"/> as an <see cref="IEditorValue"/>, normalising
/// <see cref="JsonNode"/> values into the value-currency contract:
/// <c>null | bool | string | long | double | IReadOnlyList&lt;object?&gt; |
/// IReadOnlyDictionary&lt;string,object?&gt;</c>.
/// </summary>
public sealed class ClaudeValueAdapter : IEditorValue
{
    public ClaudeValueAdapter(LayeredValue inner)
    {
        Inner = inner;
    }

    /// <summary>The underlying <see cref="LayeredValue"/> — used by the App bridge class.</summary>
    internal LayeredValue Inner { get; }

    public string Path => Inner.JsonPath;

    public IEditorScope? EffectiveScope =>
        Inner.EffectiveScope.HasValue ? ClaudeScope.For(Inner.EffectiveScope.Value) : null;

    public object? EffectiveValue => Normalise(Inner.EffectiveValue);

    public bool IsOverridden => Inner.IsOverridden;

    public object? GetValueAt(IEditorScope scope)
    {
        ConfigScope configScope = ClaudeScope.ToConfigScope(scope);
        return Normalise(Inner.GetValueAt(configScope));
    }

    public bool IsDefinedAt(IEditorScope scope)
    {
        ConfigScope configScope = ClaudeScope.ToConfigScope(scope);
        return Inner.IsDefinedAt(configScope);
    }

    /// <summary>
    /// enumerate every scope where the underlying
    /// <see cref="LayeredValue"/> has an explicit entry.  Used by the library
    /// base <see cref="LayeredEditors.Avalonia.ViewModels.PropertyEditorViewModel"/>
    /// to populate <c>OtherScopesWithData</c> on simple leaf editors so they
    /// render the "Defined in scopes:" affordance the same way compound
    /// editors already do.  <see cref="System.Linq.Enumerable.Distinct{T}(System.Collections.Generic.IEnumerable{T})"/>
    /// because <c>LayeredValue.Entries</c> can legitimately contain multiple
    /// entries at the same scope (Managed scope sees one entry per managed-
    /// settings.d/*.json drop-in).
    /// </summary>
    public IEnumerable<IEditorScope> EnumerateDefinedScopes()
    {
        return Inner.Entries
                    .Select(e => e.Scope)
                    .Distinct()
                    .Select(scope => (IEditorScope)ClaudeScope.For(scope));
    }

    // ── JsonNode ↔ currency bridge ─────────────────────────────────────────────
    //
    // The actual conversions live in JsonCurrency (Phase 2.1 step 1 split-out)
    // so consumers that don't wrap a LayeredValue can reach them too. The two
    // names below are kept as internal aliases because existing call sites
    // through this class read more naturally than direct JsonCurrency calls.

    /// <summary>
    /// Converts a <see cref="JsonNode"/> into the value-currency object graph.
    /// Delegates to <see cref="JsonCurrency.FromJsonNode"/>.
    /// </summary>
    internal static object? Normalise(JsonNode? node)
    {
        return JsonCurrency.FromJsonNode(node);
    }

    /// <summary>
    /// Converts a currency-contract value back to a <see cref="JsonNode"/>.
    /// Delegates to <see cref="JsonCurrency.ToJsonNode"/>.  Used by
    /// <see cref="ClaudeWorkspaceAdapter.SetValue"/> and by the App-bridge
    /// editor's <c>LoadFromValue</c> fallback.
    /// </summary>
    internal static JsonNode? Coerce(object? value)
    {
        return JsonCurrency.ToJsonNode(value);
    }
}
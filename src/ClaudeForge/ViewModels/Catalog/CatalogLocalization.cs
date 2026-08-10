using Bennewitz.Ninja.ClaudeForge.Localization;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels.Catalog;

/// <summary>
/// GUI-only seam that maps model-catalog ids (returned by the SDK
/// <c>IModelCatalogAccessor</c>) to localized display text. Localization is a
/// GUI concern — neither <c>Core</c> nor <c>Sdk</c> reference <c>Strings</c> — so
/// this lives in the app.
/// <para>
/// Every arm returns a <em>literal</em> <see cref="Strings"/> member so the
/// build-time dead-string guard sees each key as referenced — a reflective
/// by-name resource lookup would both evade the guard and trip its
/// dynamic-access tripwire. When the catalog gains a mode id with no mapping,
/// the label falls back to the raw id rather than throwing.
/// </para>
/// </summary>
internal static class CatalogLocalization
{
    /// <summary>Friendly label for a <c>permissions.defaultMode</c> id (e.g. <c>auto</c> → "Auto").</summary>
    public static string DefaultModeLabel(string id) => id switch
    {
        // "manual" is an accepted alias of "default" (the CLI / IDE UIs relabelled
        // the mode "Manual" in v2.1.200). Same mode, so it reuses the same strings
        // rather than inventing a second set of translations — it is only ever shown
        // when a user already has it persisted; it is not offered as a choice.
        "default" or "manual" => Strings.DefaultModeClaudeDefault,
        "acceptEdits" => Strings.DefaultModeClaudeAcceptEdits,
        "plan" => Strings.DefaultModeClaudePlan,
        "auto" => Strings.DefaultModeClaudeAuto,
        "dontAsk" => Strings.DefaultModeClaudeDontAsk,
        "bypassPermissions" => Strings.DefaultModeClaudeBypass,
        "delegate" => Strings.DefaultModeClaudeDelegate,
        _ => id,
    };

    /// <summary>One-line description for a <c>permissions.defaultMode</c> id.</summary>
    public static string DefaultModeDescription(string id) => id switch
    {
        // See DefaultModeLabel: "manual" is an alias of "default".
        "default" or "manual" => Strings.DefaultModeDescDefault,
        "acceptEdits" => Strings.DefaultModeDescAcceptEdits,
        "plan" => Strings.DefaultModeDescPlan,
        "auto" => Strings.DefaultModeDescAuto,
        "dontAsk" => Strings.DefaultModeDescDontAsk,
        "bypassPermissions" => Strings.DefaultModeDescBypass,
        "delegate" => Strings.DefaultModeDescDelegate,
        _ => string.Empty,
    };
}

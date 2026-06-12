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
        "default" => Strings.DefaultModeClaudeDefault,
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
        "default" => Strings.DefaultModeDescDefault,
        "acceptEdits" => Strings.DefaultModeDescAcceptEdits,
        "plan" => Strings.DefaultModeDescPlan,
        "auto" => Strings.DefaultModeDescAuto,
        "dontAsk" => Strings.DefaultModeDescDontAsk,
        "bypassPermissions" => Strings.DefaultModeDescBypass,
        "delegate" => Strings.DefaultModeDescDelegate,
        _ => string.Empty,
    };
}

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Env;

/// <summary>
/// Well-known environment-variable keys that Claude Code recognises but
/// the JSON schema does not declare as first-class properties (per the
/// schema's own <c>env</c> block: "Many environment variables provide
/// settings dimensions not available as dedicated settings.json
/// properties (e.g., thinking tokens, prompt caching, bash timeouts,
/// shell configuration)").
/// <para>
/// These constants are the single source of truth for the keys used by
/// <see cref="IEnvAccessor"/>'s typed convenience properties, the
/// Essentials page's pinned cards, and the suggested-env-vars list in
/// the existing Environment editor.  Adding a new key here is the
/// primary onboarding step for promoting an env var into the
/// "high-importance" surface.
/// </para>
/// </summary>
public static class EnvVarKey
{
    /// <summary>
    /// <c>MAX_THINKING_TOKENS</c> — caps Claude's extended-thinking
    /// budget per response.  Truncated reasoning is silent (the model
    /// just stops thinking earlier) so the wrong value produces a
    /// quality cliff with no error.  Common values: 8000 (small),
    /// 32000 (default-ish for serious work), 64000 (max).
    /// </summary>
    public const string MaxThinkingTokens = "MAX_THINKING_TOKENS";

    /// <summary>
    /// <c>CLAUDE_CODE_MAX_OUTPUT_TOKENS</c> — caps the size of a single
    /// response.  Truncated outputs are silent.  Default is roughly the
    /// model's native max; users tuning for cost or latency lower it.
    /// </summary>
    public const string MaxOutputTokens = "CLAUDE_CODE_MAX_OUTPUT_TOKENS";

    /// <summary>
    /// <c>CLAUDE_CODE_DISABLE_AUTO_MEMORY</c> — set to <c>1</c> to
    /// disable automatic capture of session context into the on-disk
    /// memory tier.  Schema property <c>autoMemoryEnabled</c> covers
    /// the same setting from the settings.json side.
    /// </summary>
    public const string DisableAutoMemory = "CLAUDE_CODE_DISABLE_AUTO_MEMORY";

    /// <summary>
    /// <c>DISABLE_AUTOUPDATER</c> — set to <c>1</c> to disable Claude
    /// Code's auto-updater.  Schema property <c>autoUpdatesChannel</c>
    /// covers a related but not identical concern (channel selection;
    /// this env var disables updates entirely).
    /// </summary>
    public const string DisableAutoUpdater = "DISABLE_AUTOUPDATER";

    /// <summary>
    /// <c>ANTHROPIC_MODEL</c> — runtime override for the active model.
    /// Schema's <c>model</c> property covers the same concern; the env
    /// var wins when both are set (per Claude Code's resolution order).
    /// </summary>
    public const string AnthropicModel = "ANTHROPIC_MODEL";

    /// <summary>
    /// Every well-known key declared above, in declaration order.  Used
    /// by the Environment editor to seed its "suggested env vars" list
    /// so these keys appear as add-row suggestions even when the user
    /// has not yet set them in any scope (and even though the JSON
    /// schema does not mention some of them by name in any property
    /// description).
    /// </summary>
    /// <remarks>
    /// without this, MAX_THINKING_TOKENS in particular
    /// would not appear on the Environment page until the user typed
    /// a value into the matching Essentials-page card, since the
    /// schema-driven extraction in
    /// <c>SchemaTreeBuilder.CollectSuggestedEnvVars</c> only finds env
    /// vars that are mentioned by name in property descriptions.
    /// </remarks>
    public static IReadOnlyList<string> AllWellKnown { get; } =
    [
        MaxThinkingTokens,
        MaxOutputTokens,
        DisableAutoMemory,
        DisableAutoUpdater,
        AnthropicModel,
    ];
}
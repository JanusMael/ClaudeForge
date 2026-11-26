using System.Globalization;
using Bennewitz.Ninja.AgentForge.Sdk.Env;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude.Env;

/// <summary>
/// Claude Code's well-known <c>env</c> keys as typed properties on the neutral
/// <see cref="IEnvAccessor"/>.
/// </summary>
/// <remarks>
/// <para>
/// ⓘ <b>These were members of <see cref="IEnvAccessor"/> until plans/00008 step 10.</b> The
/// accessor is neutral, and the keys are Claude Code's, so the dictionary surface stayed in
/// <c>AgentForge.Sdk</c> and the typed half moved here as extension properties. A caller writes
/// <c>client.Env.MaxThinkingTokens</c> exactly as before, with this namespace imported.
/// </para>
/// <para>
/// Setters accept <see langword="null"/> to mean "remove the key from the env map". Getters parse
/// leniently: an invalid stored value (the user typed <c>"abc"</c> into
/// <c>MAX_THINKING_TOKENS</c>) returns <see langword="null"/> rather than throwing, matching the
/// rest of the SDK's "best-effort read, strict write" posture.
/// </para>
/// </remarks>
public static class ClaudeEnvAccessorExtensions
{
    extension(IEnvAccessor env)
    {
        /// <summary>
        /// <see cref="EnvVarKey.MaxThinkingTokens"/> as a parsed <see cref="int"/>.
        /// <see langword="null"/> when unset OR when the stored string is not a valid base-10
        /// integer.
        /// </summary>
        public int? MaxThinkingTokens
        {
            get => ParseInt(env.Get(EnvVarKey.MaxThinkingTokens));
            set => env.Set(EnvVarKey.MaxThinkingTokens, FormatInt(value));
        }

        /// <summary>
        /// <see cref="EnvVarKey.MaxOutputTokens"/> as a parsed <see cref="int"/>.
        /// <see langword="null"/> when unset OR when the stored string is not a valid base-10
        /// integer.
        /// </summary>
        public int? MaxOutputTokens
        {
            get => ParseInt(env.Get(EnvVarKey.MaxOutputTokens));
            set => env.Set(EnvVarKey.MaxOutputTokens, FormatInt(value));
        }

        /// <summary>
        /// <see cref="EnvVarKey.DisableAutoMemory"/> parsed as a tri-state boolean. Claude Code's
        /// convention: stored as <c>"1"</c> (true) or <c>"0"</c> (false), absent for "not set /
        /// inherit". Other stored values yield <see langword="null"/>.
        /// </summary>
        public bool? DisableAutoMemory
        {
            get => ParseBool01(env.Get(EnvVarKey.DisableAutoMemory));
            set => env.Set(EnvVarKey.DisableAutoMemory, FormatBool01(value));
        }

        /// <summary>
        /// <see cref="EnvVarKey.DisableAutoUpdater"/> parsed as a tri-state boolean, with the same
        /// convention as <c>DisableAutoMemory</c>.
        /// </summary>
        public bool? DisableAutoUpdater
        {
            get => ParseBool01(env.Get(EnvVarKey.DisableAutoUpdater));
            set => env.Set(EnvVarKey.DisableAutoUpdater, FormatBool01(value));
        }

        /// <summary><see cref="EnvVarKey.AnthropicModel"/> as a free-form string.</summary>
        public string? AnthropicModel
        {
            get => env.Get(EnvVarKey.AnthropicModel);
            set => env.Set(EnvVarKey.AnthropicModel, value);
        }
    }

    /// <summary>
    /// Parse the stored env-string as a base-10 int. Lenient: returns null on whitespace, missing
    /// key, or unparseable value. Does not throw — callers of typed convenience properties should
    /// not have to defend against hand-edited bad values.
    /// </summary>
    private static int? ParseInt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
            ? n
            : null;
    }

    private static string? FormatInt(int? value)
    {
        return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : null;
    }

    /// <summary>
    /// Parse the Claude Code <c>"1"</c> / <c>"0"</c> tri-state boolean convention. Anything else
    /// (including <c>"true"</c> / <c>"false"</c> — those aren't what Claude Code uses) yields null.
    /// Strict so a hand-edited typo doesn't masquerade as a deliberate <c>false</c>.
    /// </summary>
    private static bool? ParseBool01(string? raw)
    {
        return raw switch
        {
            "1" => true,
            "0" => false,
            var _ => null,
        };
    }

    private static string? FormatBool01(bool? value)
    {
        return value switch
        {
            true => "1",
            false => "0",
            null => null,
        };
    }
}

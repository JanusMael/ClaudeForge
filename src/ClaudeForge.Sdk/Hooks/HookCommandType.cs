namespace Bennewitz.Ninja.ClaudeForge.Sdk.Hooks;

/// <summary>
/// How a <see cref="HookEvent.CommandValue"/> should be interpreted at hook
/// firing time.
/// </summary>
public enum HookCommandType
{
    /// <summary>Run a shell command.</summary>
    Command,

    /// <summary>Inject text directly into Claude's context as a prompt.</summary>
    Prompt,

    /// <summary>Open a URL in the default browser.</summary>
    Url,
}
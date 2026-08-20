namespace Bennewitz.Ninja.AgentForge.Core.Updates;

/// <summary>
/// How one app's release tags are spelled, so a repository shared by several apps can be
/// searched for <i>this</i> app's releases.
/// </summary>
/// <remarks>
/// <para>
/// A monorepo publishes every app's releases into one list. Without a scheme, "the latest
/// release" means "the latest release of anything in the repo", and each app reads whichever
/// sibling shipped most recently.
/// </para>
/// <para>
/// ⚠ <b>An app that already ships must keep publishing the tag shape its installed copies
/// recognise.</b> Adding a prefix to an app's own future tags does not fix old installs — it
/// blinds them, because their update check no longer recognises any tag as its own. That is why
/// <see cref="Unprefixed"/> exists and why it is not deprecated: the app that shipped first
/// keeps it forever, and only later apps take prefixes.
/// </para>
/// </remarks>
public sealed class ReleaseTagScheme
{
    /// <summary>
    /// Bare version tags — <c>v2026.3.810</c> or <c>2026.3.810</c>, with no app name.
    /// </summary>
    /// <remarks>
    /// Reserved for whichever app published the repository's releases before it hosted more
    /// than one. Its installed copies look for exactly this shape, so it cannot be changed
    /// without abandoning them.
    /// <para>
    /// It does <b>not</b> swallow other apps' tags: a prefixed tag leaves a non-numeric
    /// remainder and fails to parse, so an unprefixed app sees a sibling's release as
    /// unrecognised rather than as its own.
    /// </para>
    /// </remarks>
    public static ReleaseTagScheme Unprefixed { get; } = new(string.Empty);

    /// <param name="publishPrefix">
    /// The prefix this app's tags carry, e.g. <c>opencodeforge-</c>. Empty means unprefixed.
    /// </param>
    /// <param name="alsoRecognise">
    /// Additional prefixes to accept but not publish — for migrating an app from one tag shape
    /// to another while its old releases stay in the list.
    /// </param>
    public ReleaseTagScheme(string publishPrefix, params string[] alsoRecognise)
    {
        ArgumentNullException.ThrowIfNull(publishPrefix);
        ArgumentNullException.ThrowIfNull(alsoRecognise);

        PublishPrefix = publishPrefix;
        RecognisedPrefixes = [publishPrefix, .. alsoRecognise];
    }

    /// <summary>The prefix this app writes when it publishes a release.</summary>
    public string PublishPrefix { get; }

    /// <summary>Every prefix this app treats as its own, publish prefix first.</summary>
    public IReadOnlyList<string> RecognisedPrefixes { get; }

    /// <summary>
    /// Whether <paramref name="tag"/> is one of this app's releases, and if so its version.
    /// </summary>
    /// <remarks>
    /// The version must parse for the tag to be claimed, which is what keeps a prefixed
    /// sibling's tag from matching an unprefixed scheme.
    /// </remarks>
    public bool TryParseVersion(string? tag, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        foreach (string prefix in RecognisedPrefixes)
        {
            if (prefix.Length > 0 && !tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (GithubReleaseChecker.TryParseTag(tag[prefix.Length..], out version))
            {
                return true;
            }
        }

        version = null;
        return false;
    }
}

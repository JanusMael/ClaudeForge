using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Platform;

namespace Bennewitz.Ninja.ClaudeForge.Sdk.Claude;

/// <summary>
/// Claude Code's and Claude Desktop's product descriptors, and the bundled files they read: the two
/// schemas, Claude Code's overlay and its enum descriptions, embedded in this assembly.
/// </summary>
/// <remarks>
/// <para>
/// These lived on <c>SchemaRegistry</c> in <c>AgentForge.Core</c> until plans/00008 step 6b-3. They
/// are declared once, here, instead of being re-stated as a ternary at each call site that used to
/// branch on an <c>isClaudeCode</c> flag.
/// </para>
/// <para>
/// ⛔⛔ <b>There is deliberately NO static <c>ClaudeCodeProduct</c>, and the cheaper design was
/// measured before being refused.</b> Splitting identity from layout — a static field for the
/// ~90 sites that read only <see cref="ProductDescriptor.Id"/> or
/// <see cref="ProductDescriptor.ArchiveFolder"/>, and this method for the two engines that
/// need destinations — looked free. It is not: <see cref="ProductDescriptor.Backup"/> is
/// <c>BackupLayout ?? ProductBackupLayout.Empty</c>, so a descriptor that lost its layout
/// yields <b>zero sections</b>, and an archive written from it contains nothing while every
/// surface reports success. A null layout cannot be used as a loud failure because it is not
/// one.
/// </para>
/// <para>
/// ⚠ So the environment is required to obtain the descriptor at all, and the compiler is what
/// enforces it. <see cref="ClaudeDesktopProduct"/> stays a static field by contrast, because none of
/// its paths live under the Claude home and <c>CLAUDE_CONFIG_DIR</c> cannot move them.
/// </para>
/// <para>
/// ⓘ Equality was never the hazard: <c>BackupRequest.Includes</c> compares on
/// <see cref="ProductDescriptor.Id"/> precisely so a separately-constructed descriptor for the
/// same product still matches.
/// </para>
/// </remarks>
public static class ClaudeProducts
{
    /// <summary>Where Claude Code's settings schema is published upstream.</summary>
    public const string ClaudeCodeSettingsSchemaUrl = "https://json.schemastore.org/claude-code-settings.json";

    /// <summary>The archive folder Claude Code's sections live under.</summary>
    /// <remarks>
    /// <para>
    /// ⭐ <b>A constant so an identity-only caller never has to conjure a descriptor.</b> Several
    /// sites want nothing but this string — they are naming a path inside the archive, which no
    /// environment can move. Building a whole <see cref="ProductDescriptor"/> to read it would
    /// force an environment on code that has none, and the tempting shortcut there is an
    /// <see cref="ClaudeEnvironment.Empty"/> descriptor that later reaches a backup engine.
    /// </para>
    /// <para>
    /// ⚠ <see cref="ClaudeCodeProductFor"/> is built FROM this, so the two cannot drift.
    /// </para>
    /// <para>
    /// ⛔ <b><c>static readonly</c>, deliberately NOT <c>const</c>, for two independent reasons.</b>
    /// A <c>const</c> is baked into every consumer's IL at compile time, so changing it in a
    /// published package would not reach an assembly already compiled against the old value —
    /// which is precisely the failure mode an immutable feed makes permanent. And it folded the
    /// value-pinning assertions in <c>ExportManifestTests</c> and <c>BackupEngineTests</c> into
    /// compile-time constants, which <c>MSTEST0032</c> correctly called always-true.
    /// </para>
    /// </remarks>
    public static readonly string ClaudeCodeArchiveFolder = "ClaudeCode";

    /// <summary>The stable id of the Claude Code product, for identity comparisons.</summary>
    /// <remarks>Same reasoning as <see cref="ClaudeCodeArchiveFolder"/>, and the descriptor is
    /// built from it.</remarks>
    public static readonly string ClaudeCodeProductId = "claude-code";

    /// <summary>
    /// Where both descriptors find their bundled schemas, overlay and enum descriptions: this
    /// assembly's manifest.
    /// </summary>
    /// <remarks>
    /// ⚠ Declared before <see cref="ClaudeDesktopProduct"/> on purpose: static fields initialise in
    /// text order, and the Desktop descriptor reads this one.
    /// </remarks>
    public static readonly IBundledFiles BundledFiles = new EmbeddedResourceFiles(
        typeof(ClaudeProducts).Assembly, ClaudeResources.SchemasPrefix, ClaudeResources.DescriptionsPrefix);

    /// <summary>Claude Code's descriptor, with its home resolved from <paramref name="env"/>.</summary>
    public static ProductDescriptor ClaudeCodeProductFor(ClaudeEnvironment env) =>
        new(ClaudeCodeProductId, "Claude Code", ClaudeCodeSettingsSchemaUrl, "claude-code-settings.json",
            ArchiveFolder: ClaudeCodeArchiveFolder,
            BackupLayout: new ProductBackupLayout(
                Sections:
                [
                    ProductArchiveSection.File("claude.json", () => PlatformPaths.ClaudeJsonPath,
                        "Restoring claude.json…"),
                    // Home, not Directory: this walk honours SkippedSubdirs below.
                    ProductArchiveSection.Home("claude-dir", () => PlatformPaths.ClaudeHome(env),
                        "Restoring the Claude home directory…"),
                ],
                SkippedSubdirs:
                [
                    new("backups", "Our own backup output — skipped so backups never nest."),
                    new("projects", "Session transcripts; large. Standard skips them, Full keeps them.",
                        IncludedInFullBackup: true),
                    new("cache", "Schema / app cache — regenerated on demand; not config data."),
                    new("downloads",
                        "Downloaded update binaries: large, platform-specific, replaced by the updater."),
                    new("statsig",
                        "Telemetry / feature-flag state, regenerated on next launch; never user-authored."),
                    new("shell-snapshots", "Ephemeral shell-command snapshots, not config data."),
                    new("local",
                        "The Claude Code binary install dir — large, platform-specific, reinstalled by the updater."),
                ],
                CredentialFileName: ".credentials.json",
                ValidatableFiles:
                [
                    // claude.json at the product root, deliberately NOT recursive, or a claude.json
                    // captured under projects/ would be validated as a settings file.
                    new([], ["claude.json"], Recursive: false),
                    // settings.json / settings.local.json anywhere under the home section.
                    new(["claude-dir"], ["settings.json", "settings.local.json"], Recursive: true),
                ]))
        {
            BundledFiles = BundledFiles,
        };

    /// <summary>Claude Desktop's descriptor.</summary>
    /// <remarks>
    /// Its schema has no upstream (<c>$id</c> is a bare token), so its URL is <c>bundled://…</c>.
    /// </remarks>
    public static readonly ProductDescriptor ClaudeDesktopProduct =
        new("claude-desktop", "Claude Desktop", "bundled://claude-desktop-config", "claude-desktop-config.json",
            ArchiveFolder: "ClaudeDesktop",
            BackupLayout: new ProductBackupLayout(
                Sections:
                [
                    ProductArchiveSection.File("claude_desktop_config.json", () => PlatformPaths.DesktopConfigPath,
                        "Restoring Desktop config…"),
                    ProductArchiveSection.Directory("profiles", () => PlatformPaths.DesktopProfilesDirectory,
                        "Restoring Desktop profiles…"),
                    ProductArchiveSection.File(".desktop-current",
                        () => PlatformPaths.DesktopCurrentProfileFilePath, "Restoring Desktop active profile…"),
                ],
                // Desktop's config is a flat file plus a profiles directory; it has no home tree to
                // walk, so there is nothing to skip. An empty list is the honest answer — not an
                // oversight.
                SkippedSubdirs: [],
                ValidatableFiles:
                [
                    new([], ["claude_desktop_config.json"], Recursive: false),
                    // Every profile config, whatever it is named.
                    new(["profiles"], ["*.json"], Recursive: true),
                ]))
        {
            BundledFiles = BundledFiles,
        };
}

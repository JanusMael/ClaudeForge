using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

namespace Bennewitz.Ninja.OpenCode.Sdk;

/// <summary>
/// The two products this SDK speaks for: OpenCode's main config and its TUI config.
/// </summary>
/// <remarks>
/// <para>
/// Declared here rather than on <c>SchemaRegistry</c>, where Claude's two live. That
/// placement is a documented compromise from Phase 4 — the URLs and file names were already
/// hardcoded throughout <c>AgentForge.Core</c>, so concentrating them there made the
/// eventual split one thing to move instead of five branches to find. There is no such
/// history for OpenCode, so its descriptors start where they belong: in the product's own
/// assembly, leaving the neutral core with no OpenCode vocabulary at all.
/// </para>
/// <para>
/// They are two <i>products</i>, not one product with two files, because they are exactly
/// what <see cref="ProductDescriptor"/> describes: separate schemas, separate config files,
/// zero key overlap between them.
/// </para>
/// </remarks>
public static class OpenCodeProducts
{
    /// <summary>
    /// OpenCode's main configuration — <c>opencode.json</c> / <c>opencode.jsonc</c>.
    /// </summary>
    /// <remarks>
    /// The bundled schema is upstream's with four external <c>models.dev</c> <c>$ref</c>s
    /// stripped; see <c>BundledOpenCodeSchemaTests</c> for why, and for the guard that makes
    /// a refresh which forgets fail the build.
    /// </remarks>
    public static readonly ProductDescriptor Config =
        new("opencode", "OpenCode", "https://opencode.ai/config.json", "opencode-config.json",
            ArchiveFolder: "OpenCode",
            BackupLayout: new ProductBackupLayout(
                Sections:
                [
                    // ⭐ The whole config root as ONE directory section, NOT a Home walk with a
                    // skip list. ZipArchiveWriter reads the `.gitignore` in the directory it is
                    // handed, and OpenCode maintains one there listing exactly the regenerable
                    // files — node_modules (52.5 MiB, 99.97% of the root), package.json,
                    // package-lock.json, bun.lock and the .gitignore itself. Honouring that file
                    // is self-maintaining; transcribing its five entries into a skip list here is
                    // what made the earlier plan draft wrong, twice.
                    //
                    // ⚠ This also carries `plugins/`, which is user-authored and irreplaceable and
                    // is named by no exclusion list.
                    // ⛔⛔ GlobalDirectory(env), NOT DefaultGlobalDirectory(). This section read the
                    // default root until 2026-09-12, which meant a user with $OPENCODE_CONFIG_DIR
                    // set backed up NOTHING: measured, the archive's complete contents were
                    // Schemas/opencode-config.json and manifest.json, and the page said "Backup
                    // saved". Every read path in this SDK resolves through GlobalDirectory; the one
                    // write path that mattered did not. `OpenCodeBackupRoundTripTests` could not see
                    // it because it redirects the HOME directory and then writes into
                    // DefaultGlobalDirectory(), so the two were the same folder for the whole test.
                    ProductArchiveSection.Directory("config",
                        () => OpenCodePaths.GlobalDirectory(OpenCodeEnvironment.FromProcess()),
                        "Restoring opencode config…"),

                    // ⭐ The default root as well, because BOTH are live when they differ: the
                    // variable redirects the config that LOADS, while plugin discovery reads
                    // ~/.config/opencode regardless — measured, see OpenCodeArtifactSources and
                    // DefaultGlobalDirectory's own remarks. An archive that captured only one of
                    // them would lose a real half of the install.
                    //
                    // ⚠ Gated, because with the variable unset the two resolve to the same
                    // directory and an ungated second section would write every file twice, under
                    // two names, and restore it twice. The predicate runs at BACKUP time only —
                    // a machine restoring this archive gets `config-default/` applied whenever the
                    // archive carries it, whatever its own environment says.
                    ProductArchiveSection.DirectoryWhen("config-default",
                        () => OpenCodePaths.DefaultGlobalDirectory(),
                        "Restoring the default opencode config root…",
                        () => !SameDirectory(
                            OpenCodePaths.GlobalDirectory(OpenCodeEnvironment.FromProcess()),
                            OpenCodePaths.DefaultGlobalDirectory())),

                    // ⛔ The database and BOTH its sidecars, or none of them. A copy of
                    // opencode.db without its -wal is a stale snapshot by construction
                    // (journal_mode=wal), and merely OPENING a -wal database checkpoints it —
                    // a write. These are byte copies; nothing here opens SQLite.
                    //
                    // ⛔ Credential-bearing: account and control_account hold access_token and
                    // refresh_token, credential holds `value`, session_share holds `secret`.
                    // OpenCodeSecretColumns pins that claim and OpenCodeDatabaseSchemaTests
                    // reddens if upstream's schema drifts from it. Sanitized cannot strip SQLite
                    // rows, so these are excluded there outright rather than pretended over.
                    ProductArchiveSection.CredentialFile("data/opencode.db",
                        () => Path.Combine(OpenCodePaths.DataDirectory(), "opencode.db"),
                        "Restoring opencode.db…"),
                    ProductArchiveSection.CredentialFile("data/opencode.db-wal",
                        () => Path.Combine(OpenCodePaths.DataDirectory(), "opencode.db-wal"),
                        "Restoring opencode.db-wal…"),
                    ProductArchiveSection.CredentialFile("data/opencode.db-shm",
                        () => Path.Combine(OpenCodePaths.DataDirectory(), "opencode.db-shm"),
                        "Restoring opencode.db-shm…"),
                ],
                // Empty: the `.gitignore` in the config root does this work, and reading it is the
                // decision — see the section comment above.
                SkippedSubdirs: [],
                // ⛔ NOT auth.json. It is absent from an install that has never signed in, which is
                // not evidence it has gone away, so it stays excluded entirely rather than being
                // declared as an opt-in section. It lives in the data root, outside the config
                // directory this layout archives, so nothing picks it up by accident either.
                CredentialFileName: null));

    /// <summary>
    /// OpenCode's terminal-UI configuration — <c>tui.json</c>. Theme, 184 keybind actions,
    /// cursor, mouse and scroll behaviour. No key overlap with <see cref="Config"/>.
    /// </summary>
    public static readonly ProductDescriptor Tui =
        new("opencode-tui", "OpenCode TUI", "https://opencode.ai/tui.json", "opencode-tui.json",
            ArchiveFolder: "OpenCodeTui");

    /// <summary>Both products, in the order a host should present them.</summary>
    public static IReadOnlyList<ProductDescriptor> All { get; } = [Config, Tui];

    /// <summary>
    /// Whether two paths name the same directory on this machine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Normalised through <see cref="Path.GetFullPath(string)"/> first, so a trailing separator, a
    /// mixed separator or a <c>..</c> segment in <c>$OPENCODE_CONFIG_DIR</c> cannot make the two
    /// roots look different and archive the same files twice.
    /// </para>
    /// <para>
    /// ⚠ <b>Asks the real OS, not <c>PlatformInfo.Current</c>.</b> That probe is a simulation seam
    /// — the <c>--windows</c> / <c>--macos</c> / <c>--linux</c> debug flags drive it, so a user
    /// exercising platform-conditional UI could otherwise flip how the actual file system's paths
    /// compare. Case sensitivity here is a fact about the disk, not about the layout being
    /// previewed.
    /// </para>
    /// </remarks>
    private static bool SameDirectory(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Backup;

namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Backup;

/// <summary>
/// Everything the Backup / Restore page needs from its host.
/// </summary>
/// <remarks>
/// <para>
/// An options record rather than four more constructor parameters: the page already takes a dialog
/// service and an optional share service, and the repo caps positional parameters at six.
/// </para>
/// <para>
/// ⛔ <b>Every member is <c>required</c> on purpose.</b> A host that adds a product must state what
/// it backs up, what it calls things, and which processes to warn about — a missing entry is a
/// compile error rather than another product's defaults leaking in. This page used to default to
/// Claude's two products and Claude's process names; nothing about that failed loudly.
/// </para>
/// </remarks>
public sealed record BackupPageOptions
{
    /// <summary>The page's wording, supplied from the host's own resx.</summary>
    public required BackupPageText Text { get; init; }

    /// <summary>
    /// The engine this page creates and restores through.
    /// </summary>
    /// <remarks>
    /// ⛔ <b>NOT <see cref="BackupEngine.Default"/> for every host.</b> A backup takes its products
    /// from the request, so the default engine writes any product's archive happily — and restores
    /// only Claude's, reporting success while returning nothing. A host must pass an engine whose
    /// restorable products match what it backs up. See <c>OpenCodeBackup.Engine</c>.
    /// </remarks>
    public required BackupEngine Engine { get; init; }

    /// <summary>
    /// The products this host can back up, in the order their checkboxes render.
    /// </summary>
    public required IReadOnlyList<ProductDescriptor> Products { get; init; }

    /// <summary>
    /// Process names to check before a restore, so the user gets an early heads-up that files may
    /// be locked — <c>"claude"</c> and <c>"claude-desktop"</c> for ClaudeForge,
    /// <c>"opencode"</c> for OpenCodeForge. Compared without extension, as
    /// <see cref="System.Diagnostics.Process.GetProcessesByName(string)"/> expects.
    /// </summary>
    /// <remarks>
    /// ⚠ Advisory only. File-lock conflicts during a restore are still handled per-file by the
    /// engine; this is a heads-up, never a block, and an empty list is a valid choice.
    /// </remarks>
    public required IReadOnlyList<string> AgentProcessNames { get; init; }

    /// <summary>
    /// The localized include-in-backup checkbox label for a product.
    /// </summary>
    /// <remarks>
    /// A delegate rather than a string per product, because the set of products is the host's to
    /// decide. Returning <see cref="ProductDescriptor.DisplayName"/> is a reasonable fallback for a
    /// host with no translated label — visibly English rather than wrong.
    /// </remarks>
    public required Func<ProductDescriptor, string> ProductCheckboxLabel { get; init; }
}

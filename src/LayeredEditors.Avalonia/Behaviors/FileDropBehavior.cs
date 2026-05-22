using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Behaviors;

/// <summary>
/// Attached behaviour that wires drag-drop of files into a single
/// <see cref="ICommand"/> on the bound view-model.  Hides the Avalonia 12
/// <see cref="IDataTransfer"/> / <see cref="DataFormat.File"/> surface so
/// every drop-target in the app gets:
///
/// <list type="bullet">
///   <item><see cref="DragDrop.SetAllowDrop"/> set to <see langword="true"/> automatically.</item>
///   <item><c>DragOver</c> filters by extension so the OS cursor shows
///   <c>Copy</c> only for accepted payloads and <c>None</c> otherwise.</item>
///   <item><c>Drop</c> extracts the local filesystem path from the dropped
///   <c>IStorageItem</c> and invokes <see cref="DropCommandProperty"/>
///   with the path string as the command parameter.</item>
/// </list>
///
/// Opt-in per control via the three attached properties:
/// <code>
/// &lt;DockPanel
///     behaviors:FileDrop.AllowedExtensions="zip"
///     behaviors:FileDrop.DropCommand="{Binding RestoreFromDroppedArchiveCommand}" /&gt;
/// </code>
///
/// <para>
/// <b>Extension matching:</b> <see cref="AllowedExtensionsProperty"/> is a
/// comma-separated list of extensions WITHOUT leading dots
/// (<c>"zip"</c> / <c>"zip,json"</c>).  Matching is case-insensitive
/// (drops from "FILE.ZIP" work).  Empty / null = accept any file.
/// </para>
///
/// <para>
/// <b>Multi-file drops:</b> currently single-file only — the first matching
/// file in the payload is used and any extras are silently dropped.  This
/// matches the Avalonia 12 behaviour where Windows / X11 platforms surface
/// multiple files at once but the typical UX gesture is single-file.  If a
/// future consumer needs multi-file semantics, add an <c>AllowMultiple</c>
/// property and a sibling <c>DropFilesCommand</c> that takes
/// <c>IReadOnlyList&lt;string&gt;</c>.
/// </para>
///
/// <para>
/// <b>Avalonia 12 note:</b> the drag-drop payload surface migrated from
/// <c>IDataObject</c> / <c>DataFormats.Files</c> in Avalonia 11 to
/// <see cref="IDataTransfer"/> / <see cref="DataFormat.File"/> in 12.  This
/// behaviour uses the new surface; do NOT pair it with the legacy
/// <c>e.Data.GetFiles()</c> path.
/// </para>
///
/// <para>
/// See <c>DataGridCopyValueBehavior.cs</c> for the codebase's preferred
/// attached-property style (static class with <c>RegisterAttached</c>
/// properties + a static ctor that subscribes to <c>.Changed</c>).
/// </para>
/// </summary>
public static class FileDrop
{
    // ── Attached properties ──────────────────────────────────────────────────

    /// <summary>
    /// Comma-separated list of extensions to accept, without leading dots
    /// (e.g. <c>"zip"</c> or <c>"zip,json"</c>).  Case-insensitive.  Null or
    /// empty = accept any file.  Setting this property also wires the
    /// drop-handlers on the host control if <see cref="DropCommandProperty"/>
    /// is also set.
    /// </summary>
    public static readonly AttachedProperty<string?> AllowedExtensionsProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("AllowedExtensions", typeof(FileDrop));

    /// <summary>
    /// Command invoked with the local filesystem path of the dropped file as
    /// its parameter (a <see cref="string"/>).  Setting this property auto-
    /// enables <see cref="DragDrop.SetAllowDrop"/> and subscribes the DragOver /
    /// Drop event handlers.
    /// </summary>
    public static readonly AttachedProperty<ICommand?> DropCommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>("DropCommand", typeof(FileDrop));

    // Sentinel to ensure event handlers are subscribed at most once per Control
    // instance, even if the attached properties are set in multiple steps.
    private static readonly AttachedProperty<bool> IsWiredProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsWired", typeof(FileDrop));

    static FileDrop()
    {
        // Subscribe on EITHER attached property change.  We re-check both
        // values inside the handler; the wiring only fires once a command is
        // present (matching extensions alone is harmless without a target).
        DropCommandProperty.Changed.AddClassHandler<Control>(OnAttachedPropertyChanged);
        AllowedExtensionsProperty.Changed.AddClassHandler<Control>(OnAttachedPropertyChanged);
    }

    public static string? GetAllowedExtensions(Control c)
    {
        return c.GetValue(AllowedExtensionsProperty);
    }

    public static void SetAllowedExtensions(Control c, string? value)
    {
        c.SetValue(AllowedExtensionsProperty, value);
    }

    public static ICommand? GetDropCommand(Control c)
    {
        return c.GetValue(DropCommandProperty);
    }

    public static void SetDropCommand(Control c, ICommand? value)
    {
        c.SetValue(DropCommandProperty, value);
    }

    // ── Wiring ───────────────────────────────────────────────────────────────

    private static void OnAttachedPropertyChanged(Control c, AvaloniaPropertyChangedEventArgs e)
    {
        // Wire once.  Re-wiring on every property change would either
        // duplicate handlers or require careful unsubscribe-on-old-value
        // bookkeeping; one-shot wiring is simpler and the per-event
        // handlers read the current property values on each invocation.
        if (c.GetValue(IsWiredProperty))
        {
            return;
        }

        c.SetValue(IsWiredProperty, true);

        DragDrop.SetAllowDrop(c, true);
        c.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        c.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Control c)
        {
            return;
        }

        string? allowed = GetAllowedExtensions(c);
        e.DragEffects = ContainsAcceptedFile(e.DataTransfer, allowed)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private static void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (sender is not Control c)
        {
            return;
        }

        ICommand? cmd = GetDropCommand(c);
        if (cmd is null)
        {
            return;
        }

        string? allowed = GetAllowedExtensions(c);
        string? path = TryGetFirstAcceptedPath(e.DataTransfer, allowed);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (cmd.CanExecute(path))
        {
            cmd.Execute(path);
        }
    }

    // ── Payload helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// True when the drag payload contains at least one file whose extension
    /// matches <paramref name="allowedExtensionsCsv"/>.  Used by DragOver so
    /// the OS cursor reflects accept / reject before the user lets go of
    /// the mouse button.
    /// </summary>
    internal static bool ContainsAcceptedFile(IDataTransfer? data, string? allowedExtensionsCsv)
    {
        if (data is null)
        {
            return false;
        }

        if (!data.Contains(DataFormat.File))
        {
            return false;
        }

        IStorageItem[]? files = data.TryGetFiles();
        if (files is null)
        {
            return false;
        }

        string[]? allowed = ParseExtensions(allowedExtensionsCsv);
        foreach (var item in files)
        {
            if (item is null)
            {
                continue;
            }

            if (HasAcceptedExtension(item.Name, allowed))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Extracts the first dropped file whose extension matches
    /// <paramref name="allowedExtensionsCsv"/>, returning its local
    /// filesystem path (<see cref="Uri.LocalPath"/>) or <see langword="null"/>.
    /// </summary>
    internal static string? TryGetFirstAcceptedPath(IDataTransfer? data, string? allowedExtensionsCsv)
    {
        if (data is null)
        {
            return null;
        }

        if (!data.Contains(DataFormat.File))
        {
            return null;
        }

        IStorageItem[]? files = data.TryGetFiles();
        if (files is null)
        {
            return null;
        }

        string[]? allowed = ParseExtensions(allowedExtensionsCsv);
        foreach (var item in files)
        {
            if (item is null)
            {
                continue;
            }

            if (!HasAcceptedExtension(item.Name, allowed))
            {
                continue;
            }

            try
            {
                Uri local = item.Path;
                if (local.IsAbsoluteUri && local.IsFile)
                {
                    return local.LocalPath;
                }
            }
            catch (Exception ex) when (ex is UriFormatException or InvalidOperationException)
            {
                // Item.Path is occasionally non-file (e.g. some Linux DEs
                // surface synthesised URIs).  Skip and try the next item.
                _ = ex;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses the comma-separated extension list into a normalised set:
    /// lowercased, leading dot stripped if present, empty entries dropped.
    /// Returns <see langword="null"/> when the input is null/empty/whitespace,
    /// signalling "accept any extension."
    /// </summary>
    internal static string[]? ParseExtensions(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return null;
        }

        return csv
               .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
               .Select(NormaliseExt)
               .Where(s => s.Length > 0)
               .ToArray();
    }

    private static string NormaliseExt(string raw)
    {
        string s = raw.Trim().TrimStart('.');
        return s.ToLowerInvariant();
    }

    internal static bool HasAcceptedExtension(string fileName, string[]? allowed)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        // Null allowed-list = accept any file.
        if (allowed is null || allowed.Length == 0)
        {
            return true;
        }

        int dot = fileName.LastIndexOf('.');
        if (dot < 0)
        {
            return false;
        }

        string ext = fileName[(dot + 1)..].ToLowerInvariant();
        return allowed.Contains(ext);
    }
}
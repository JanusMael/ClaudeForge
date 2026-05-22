using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Behaviors;

/// <summary>
/// Attached behaviour that adds a "Copy value" context-menu item to any DataGrid.
/// Opt-in per DataGrid: <c>behaviors:CopyValue.IsEnabled="True"</c>.
/// If the DataGrid already has a ContextMenu the item is appended after a separator,
/// preserving any existing items.
/// </summary>
public static class CopyValue
{
    private static readonly ConditionalWeakTable<DataGrid, LastCellHolder> s_last = new();

    private sealed class LastCellHolder
    {
        public DataGridCell? Cell;
    }

    // ── Attached properties ──────────────────────────────────────────────────

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, bool>("IsEnabled", typeof(CopyValue));

    // Sentinel that prevents AttachCopyMenuItem from running more than once per DataGrid instance.
    private static readonly AttachedProperty<bool> IsMenuAttachedProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, bool>("IsMenuAttached", typeof(CopyValue));

    static CopyValue()
    {
        IsEnabledProperty.Changed.AddClassHandler<DataGrid>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(DataGrid dg)
    {
        return dg.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(DataGrid dg, bool value)
    {
        dg.SetValue(IsEnabledProperty, value);
    }

    // ── Wiring ───────────────────────────────────────────────────────────────

    private static void OnIsEnabledChanged(DataGrid dg, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
        {
            return;
        }

        dg.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed,
            RoutingStrategies.Bubble, handledEventsToo: true);

        // Defer ContextMenu modification until the control loads so that any
        // ContextMenu declared in AXAML is already attached.
        dg.Loaded += OnLoaded;
    }

    private static void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not DataGrid dg)
        {
            return;
        }

        dg.Loaded -= OnLoaded;
        AttachCopyMenuItem(dg);
    }

    private static void AttachCopyMenuItem(DataGrid dg)
    {
        if (dg.GetValue(IsMenuAttachedProperty))
        {
            return;
        }

        dg.SetValue(IsMenuAttachedProperty, true);

        MenuItem copyItem = new() { Header = "Copy value" };
        copyItem.Click += async (_, _) => await CopyCellAsync(dg);

        if (dg.ContextMenu is null)
        {
            dg.ContextMenu = new ContextMenu();
        }
        else
        {
            dg.ContextMenu.Items.Add(new Separator());
        }

        dg.ContextMenu.Items.Add(copyItem);
    }

    // ── Pointer handler ──────────────────────────────────────────────────────

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not DataGrid dg)
        {
            return;
        }

        if (!e.GetCurrentPoint(null).Properties.IsRightButtonPressed)
        {
            return;
        }

        Visual? source = e.Source as Visual;
        DataGridCell? cell = source is DataGridCell c
            ? c
            : source?.GetVisualAncestors().OfType<DataGridCell>().FirstOrDefault();

        s_last.GetOrCreateValue(dg).Cell = cell;
    }

    // ── Copy logic ───────────────────────────────────────────────────────────

    private static async Task CopyCellAsync(DataGrid dg)
    {
        if (!s_last.TryGetValue(dg, out LastCellHolder? holder) || holder.Cell is null)
        {
            return;
        }

        string? text = ExtractText(holder.Cell);
        if (text is null)
        {
            return;
        }

        IClipboard? clipboard = TopLevel.GetTopLevel(dg)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private static string? ExtractText(DataGridCell cell)
    {
        // For DataGridTextColumn and DataGridTemplateColumn (incl. TipCell wrappers),
        // the visible text always lives in the first TextBlock descendant.
        TextBlock? tb = cell.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault();
        return tb?.Text ?? cell.Content?.ToString();
    }
}
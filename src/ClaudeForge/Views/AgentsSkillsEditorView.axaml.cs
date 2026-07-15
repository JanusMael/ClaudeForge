using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Bennewitz.Ninja.ClaudeForge.ViewModels;

namespace Bennewitz.Ninja.ClaudeForge.Views;

/// <summary>
/// "Agents &amp; Skills" page — single nav node with an in-page segmented
/// control (Sub-agents / Skills / Slash Commands).  Bound to
/// <see cref="Bennewitz.Ninja.ClaudeForge.ViewModels.AgentsSkillsEditorViewModel"/>.
///
/// Navigation, refresh, reveal / open-externally, row selection, edit /
/// save / cancel, and the raw-mode toggle are all bindings-driven via the
/// VM's commands.  The only code-behind is <see cref="OnRawKeyDown"/>, which
/// adds smart indentation to the raw front-matter editor (a behaviour that
/// needs <see cref="KeyEventArgs"/>, which a binding can't supply) — no new
/// dependency, no syntax-highlighting library (AvaloniaEdit is incompatible
/// with Semi.Avalonia; see CLAUDE.md "Common gotchas").
/// </summary>
public partial class AgentsSkillsEditorView : UserControl
{
    private const string IndentUnit = "  "; // 2 spaces — YAML uses spaces, never tabs

    private AgentsSkillsEditorViewModel? _vm;

    public AgentsSkillsEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    // Clipboard bridge for the VM's "Copy markdown" command — keeps the VM free of
    // any TopLevel dependency (mirrors MemoryEditorView).
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.CopyMarkdownRequested -= OnCopyMarkdownRequested;
        }

        _vm = DataContext as AgentsSkillsEditorViewModel;

        if (_vm is not null)
        {
            _vm.CopyMarkdownRequested += OnCopyMarkdownRequested;
        }
    }

    private async void OnCopyMarkdownRequested(object? sender, string markdown)
    {
        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(markdown);
        }
    }

    /// <summary>
    /// Smart indentation for the raw front-matter TextBox:
    /// <list type="bullet">
    ///   <item><b>Tab</b> inserts two spaces.</item>
    ///   <item><b>Shift+Tab</b> removes up to two leading spaces from the
    ///         current line (dedent).</item>
    ///   <item><b>Enter</b> inserts a newline preserving the current line's
    ///         leading whitespace (auto-indent).</item>
    /// </list>
    /// </summary>
    private void OnRawKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        string text = box.Text ?? string.Empty;
        int caret = Math.Clamp(box.CaretIndex, 0, text.Length);

        switch (e.Key)
        {
            case Key.Tab when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
            {
                // Dedent: drop up to IndentUnit leading spaces on the caret's line.
                int lineStart = text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
                int removable = 0;
                while (removable < IndentUnit.Length
                       && lineStart + removable < text.Length
                       && text[lineStart + removable] == ' ')
                {
                    removable++;
                }

                if (removable > 0)
                {
                    box.Text = text.Remove(lineStart, removable);
                    box.CaretIndex = Math.Max(lineStart, caret - removable);
                }

                e.Handled = true;
                break;
            }

            case Key.Tab:
            {
                box.Text = text.Insert(caret, IndentUnit);
                box.CaretIndex = caret + IndentUnit.Length;
                e.Handled = true;
                break;
            }

            case Key.Enter or Key.Return:
            {
                // Auto-indent: carry the current line's leading whitespace.
                int lineStart = text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
                int ws = lineStart;
                while (ws < text.Length && ws < caret && (text[ws] == ' ' || text[ws] == '\t'))
                {
                    ws++;
                }

                string indent = text[lineStart..ws];
                string insert = "\n" + indent;
                box.Text = text.Insert(caret, insert);
                box.CaretIndex = caret + insert.Length;
                e.Handled = true;
                break;
            }
        }
    }
}
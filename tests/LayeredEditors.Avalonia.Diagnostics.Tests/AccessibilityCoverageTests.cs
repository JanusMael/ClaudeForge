using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.Dialogs;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.Logging;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.UI;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.Tests;

/// <summary>
/// Guards the accessibility invariant from the root <c>AGENTS.md</c> for the UI this library
/// builds in C#: every interactive control MUST carry a non-empty
/// <c>AutomationProperties.Name</c>, in clean text, so a screen reader has something to
/// announce. <c>ClaudeForge.Tests</c> enforces the same rule for the host's AXAML views by
/// scanning the markup (<c>AxamlAccessibilityCoverageTests</c>); there is no markup here, so
/// each window is built for real on the headless UI thread and its logical tree is walked.
/// <para>
/// The walk covers the logical tree of the unshown window plus any <see cref="ContextMenu"/>
/// hung off a control, because a context menu joins the logical tree only when it opens. A
/// window is deliberately not shown: applying templates would pull Avalonia's own template
/// parts (scroll-bar repeat buttons and the like) into view, and those are not this library's
/// to name. Controls placed in a flyout or created inside a template are therefore out of the
/// walker's reach; add a root for them here if the library ever grows one.
/// </para>
/// <para>
/// Interactive means the control set the AXAML guard uses, plus <see cref="MenuItem"/>,
/// <see cref="SelectableTextBlock"/>, and a <see cref="TextBlock"/> with a hand cursor, which
/// is how the live windows draw their header links. Each test also pins the number of
/// interactive controls the walker found: a new control must bump the count, and a count that
/// comes out lower than expected means the walker can no longer reach something.
/// </para>
/// </summary>
[TestClass]
public sealed class AccessibilityCoverageTests
{
    private static HeadlessUnitTestSession Session =>
        HeadlessUnitTestSession.GetOrStartForAssembly(Assembly.GetExecutingAssembly());

    [TestMethod]
    public Task FatalErrorDialog_EveryInteractiveControlIsNamed()
    {
        return Session.Dispatch(() =>
        {
            FatalErrorDialog dialog = new("Title", "Message", new InvalidOperationException("boom"));

            // The details box, Copy to Clipboard, and Close.
            AssertEveryInteractiveControlIsNamed(dialog, expectedControls: 3);
        }, CancellationToken.None);
    }

    [TestMethod]
    public Task NonFatalNoticeDialog_EveryInteractiveControlIsNamed()
    {
        return Session.Dispatch(() =>
        {
            NonFatalNoticeDialog dialog = new("Title", "Message", "line 1\nline 2");

            // The details box, Copy to Clipboard, and Close.
            AssertEveryInteractiveControlIsNamed(dialog, expectedControls: 3);
        }, CancellationToken.None);
    }

    [TestMethod]
    public Task LiveTailWindow_EveryInteractiveControlIsNamed()
    {
        return Session.Dispatch(() =>
        {
            LiveTailWindow tail = new("Live Events");

            // The selectable text surface and the Clear link.
            AssertEveryInteractiveControlIsNamed(tail.WindowForTesting, expectedControls: 2);
        }, CancellationToken.None);
    }

    [TestMethod]
    public Task LiveLogWindow_EveryInteractiveControlIsNamed()
    {
        return Session.Dispatch(() =>
        {
            // Initialize first, as a host or another test in this process would have, so the
            // rebuild below proves it replaces an already-latched window. The log-file and
            // Open-folder links render only when a sink and a logs directory are supplied, and
            // the launch link only with a label and an action, so the rebuild passes all three.
            LiveLogWindow.Initialize();

            string sandbox = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string logsDirectory = Path.Combine(sandbox, "logs");
            Directory.CreateDirectory(logsDirectory);
            try
            {
                using BucketedRollingFileSink sink = new(logsDirectory);
                Window window = LiveLogWindow.RebuildWindowForTesting(
                    sink,
                    logsDirectory,
                    extraActionLabel: "Events",
                    extraAction: () => { });

                // The log list, its Copy menu item, and the log-file, Open-folder, and launch links.
                AssertEveryInteractiveControlIsNamed(window, expectedControls: 5);
            }
            finally
            {
                try
                {
                    Directory.Delete(sandbox, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Best-effort sandbox cleanup; a leftover temp directory is not a test failure.
                }
            }
        }, CancellationToken.None);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void AssertEveryInteractiveControlIsNamed(Window window, int expectedControls)
    {
        List<Control> interactive = CollectInteractiveControls(window);
        List<string> failures = new();
        string owner = $"{window.GetType().Name} \"{window.Title}\"";

        foreach (Control control in interactive)
        {
            string? name = AutomationProperties.GetName(control);
            string label = $"{control.GetType().Name} \"{Describe(control)}\"";

            if (string.IsNullOrWhiteSpace(name))
            {
                failures.Add($"  • {label}: AutomationProperties.Name is not set.");
            }
            else if (name.Any(char.IsSurrogate))
            {
                // Emoji live outside the Basic Multilingual Plane, so a surrogate pair in a name
                // means a screen reader will read a glyph name aloud ("clipboard Copy").
                failures.Add($"  • {label}: AutomationProperties.Name \"{name}\" contains an emoji.");
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail(
                $"Accessibility coverage regression in {owner} — every interactive " +
                "control needs a clean-text AutomationProperties.Name:\n\n" +
                string.Join('\n', failures) +
                "\n\nFix: call AutomationProperties.SetName(control, \"...\") where the control is " +
                "built (plus SetHelpText when the visible label is ambiguous). See the " +
                "accessibility invariant in the root AGENTS.md.\n");
        }

        Assert.AreEqual(expectedControls, interactive.Count,
            $"{owner}: the walker found {interactive.Count} interactive controls " +
            $"but this test expects {expectedControls}. If you added or removed a control, update " +
            "the expected count here. If the count dropped without a removal, the walker no longer " +
            "reaches a control (a flyout or template root, for example) and needs a new root.");
    }

    /// <summary>
    /// Every interactive control reachable from <paramref name="window"/> through the logical
    /// tree, descending into each control's <see cref="Control.ContextMenu"/> as an extra root.
    /// </summary>
    private static List<Control> CollectInteractiveControls(Window window)
    {
        List<Control> result = new();
        HashSet<Control> seen = new();
        Queue<ILogical> roots = new();
        roots.Enqueue(window);

        while (roots.Count > 0)
        {
            ILogical root = roots.Dequeue();
            foreach (ILogical logical in Enumerable.Repeat(root, 1).Concat(root.GetLogicalDescendants()))
            {
                if (logical is not Control control || !seen.Add(control))
                {
                    continue;
                }

                if (IsInteractive(control))
                {
                    result.Add(control);
                }

                if (control.ContextMenu is { } menu)
                {
                    roots.Enqueue(menu);

                    // Items added directly are logical children of the menu; enqueue them too so
                    // the walk does not depend on that detail of ItemsControl.
                    foreach (object? item in menu.Items)
                    {
                        if (item is ILogical menuItem)
                        {
                            roots.Enqueue(menuItem);
                        }
                    }
                }
            }
        }

        return result;
    }

    private static bool IsInteractive(Control control)
    {
        // Button covers ToggleButton, CheckBox, RadioButton, ToggleSwitch, and RepeatButton.
        // DataGrid is absent because this library does not reference Avalonia.Controls.DataGrid.
        return control is Button
                   or TextBox
                   or ComboBox
                   or ListBox
                   or MenuItem
                   or Slider
                   or NumericUpDown
                   or AutoCompleteBox
                   or DatePicker
                   or TimePicker
                   or CalendarDatePicker
                   or SelectableTextBlock
               || (control is TextBlock text && IsHandCursor(text.Cursor));
    }

    /// <summary>
    /// A hand cursor on a TextBlock is how the live windows mark a link-styled TextBlock that
    /// acts as a button. <see cref="Cursor"/> exposes no type accessor; its
    /// <see cref="Cursor.ToString"/> is the <see cref="StandardCursorType"/> name.
    /// </summary>
    private static bool IsHandCursor(Cursor? cursor)
    {
        return cursor is not null
               && string.Equals(cursor.ToString(), nameof(StandardCursorType.Hand), StringComparison.Ordinal);
    }

    private static string Describe(Control control)
    {
        string? text = control switch
        {
            TextBlock tb => tb.Text,
            ContentControl cc => cc.Content?.ToString(),
            MenuItem mi => mi.Header?.ToString(),
            TextBox tbx => tbx.Text,
            _ => control.Name,
        };

        text = (text ?? string.Empty).ReplaceLineEndings(" ");
        return text.Length > 40 ? text[..40] + "…" : text;
    }
}

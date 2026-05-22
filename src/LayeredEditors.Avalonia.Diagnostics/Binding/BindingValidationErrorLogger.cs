using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Serilog;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Diagnostics.Binding;

/// <summary>
/// Logs every Avalonia binding validation error that lands in
/// <see cref="DataValidationErrors.ErrorsProperty"/>, regardless of the code path
/// that produced it.
/// <para>
/// <strong>Why this exists:</strong> When Avalonia's own type-coercion step
/// (e.g. writing a <c>string</c> value into an <c>int</c>-typed binding target)
/// fails, <c>BindingExpression</c> synthesises an
/// <see cref="System.InvalidCastException"/> and calls
/// <c>OnDataValidationError(ex)</c> directly. This is <em>not</em> routed
/// through <c>Avalonia.Logging.Logger</c>, so
/// <see cref="Logging.SerilogAvaloniaSink"/> never sees it. The only observable
/// side-effect is that the exception ends up in the target control's
/// <see cref="DataValidationErrors.ErrorsProperty"/> and a red adorner is
/// rendered — the classic "adorner appears but nothing logged" symptom on
/// coercion failures.
/// </para>
/// <para>
/// <see cref="Install"/> hooks the <see cref="DataValidationErrors.ErrorsProperty"/>
/// attached-property change pipeline with a class handler so every
/// <c>Set</c>/<c>Add</c> of an error produces a structured log entry with the
/// full exception text (stack trace + inner exceptions).
/// </para>
/// </summary>
public static class BindingValidationErrorLogger
{
    private static bool _installed;

    /// <summary>
    /// Registers the class handler exactly once. Safe to call multiple times;
    /// subsequent calls are no-ops. Must be called after Avalonia framework
    /// initialisation (typically from <c>App.OnFrameworkInitializationCompleted</c>).
    /// </summary>
    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;

        // Class-level handler fires for every Control instance's
        // DataValidationErrors.ErrorsProperty transition — including the
        // coercion path in BindingExpression that bypasses IDataValidationPlugin.
        //
        // Both generic arguments are specified explicitly to select the typed
        // overload: AddClassHandler<TTarget, TValue>(...) rather than the
        // untyped AddClassHandler<TTarget>(...) the compiler would otherwise
        // pick by inference.
        DataValidationErrors.ErrorsProperty.Changed.AddClassHandler<Control, IEnumerable<object>?>(OnErrorsChanged);
    }

    private static void OnErrorsChanged(Control control, AvaloniaPropertyChangedEventArgs<IEnumerable<object>?> e)
    {
        // A null/empty new value means the errors were cleared (e.g. the user
        // fixed the input). Nothing to log.
        IEnumerable<object>? errors = e.NewValue.GetValueOrDefault();
        if (errors is null)
        {
            return;
        }

        // Only log the errors that are newly present. An already-logged error
        // object would be in the old collection too.
        IEnumerable<object>? oldErrors = e.OldValue.GetValueOrDefault();
        HashSet<object>? previouslySeen = oldErrors is null
            ? null
            : new HashSet<object>(oldErrors, ReferenceEqualityComparer.Instance);

        string controlLabel = BuildControlLabel(control);
        string dataContext = control.DataContext?.GetType().FullName ?? "(null)";
        string contextDetails = BuildContextDetails(control);

        foreach (object error in errors)
        {
            if (previouslySeen is not null && previouslySeen.Contains(error))
            {
                continue;
            }

            if (error is Exception ex)
            {
                // Most binding-pipeline exceptions here are SYNTHESISED by Avalonia
                // (e.g. `new InvalidCastException("Could not convert...")` in
                // BindingExpression.WriteValueToSource) — they are constructed,
                // never thrown, so ex.StackTrace is null. To give the log reader
                // something to go on we capture the runtime stack at the moment
                // the handler fires; it shows the Avalonia + app frames that led
                // to the error (layout/render/user-interaction path), which is
                // the only stack information that exists for this class of error.
                string liveStack = ex.StackTrace is null
                    ? new StackTrace(fNeedFileInfo: true).ToString()
                    : ex.ToString();

                Log.Warning(
                    "[BindingValidation] {Control} (DataContext={DataContext}){NewLine}  {ContextDetails}{NewLine}  {ExceptionType}: {ExceptionMessage}{NewLine}{Details}",
                    controlLabel,
                    dataContext,
                    Environment.NewLine,
                    contextDetails,
                    Environment.NewLine,
                    ex.GetType().FullName,
                    ex.Message,
                    Environment.NewLine,
                    liveStack);
            }
            else
            {
                Log.Warning(
                    "[BindingValidation] {Control} (DataContext={DataContext}){NewLine}  {ContextDetails}{NewLine}  {Error}",
                    controlLabel,
                    dataContext,
                    Environment.NewLine,
                    contextDetails,
                    Environment.NewLine,
                    error);
            }
        }
    }

    /// <summary>
    /// Builds a short descriptor for a control used in log lines — type name plus
    /// optional <see cref="StyledElement.Name"/>. Readable and stable enough to
    /// correlate a log line with a visible control without dumping the whole
    /// visual tree.
    /// </summary>
    private static string BuildControlLabel(Control control)
    {
        string typeName = control.GetType().Name;
        string? name = control.Name;
        return string.IsNullOrEmpty(name) ? typeName : $"{typeName}#{name}";
    }

    /// <summary>
    /// Emits a semicolon-separated list of identifying details about the control
    /// — everything that might help a developer find the offending element in
    /// the source XAML when all they have is a log line. Only non-empty fields
    /// are included, so the output stays compact.
    /// </summary>
    private static string BuildContextDetails(Control control)
    {
        StringBuilder sb = new();
        Append(sb, "Type", control.GetType().FullName);
        Append(sb, "AutomationId", AutomationProperties.GetAutomationId(control));
        Append(sb, "AutoName", AutomationProperties.GetName(control));
        Append(sb, "Tag", control.Tag?.ToString());

        string classes = string.Join(' ', control.Classes);
        Append(sb, "Classes", string.IsNullOrWhiteSpace(classes) ? null : classes);

        Append(sb, "Ancestors", BuildAncestorChain(control));
        Append(sb, "State", BuildControlStateSummary(control));

        return sb.Length == 0 ? "(no additional details)" : sb.ToString();

        static void Append(StringBuilder builder, string key, string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append("; ");
            }

            builder.Append(key).Append('=').Append(value);
        }
    }

    /// <summary>
    /// Walks the logical tree upward and returns the chain of named (or typed)
    /// ancestors up to six levels — enough to localise an editor page / template
    /// instance without dumping the entire tree. Format:
    /// <c>OuterName#Outer &gt; InnerName#Inner</c>, root-first.
    /// </summary>
    private static string? BuildAncestorChain(Control control)
    {
        List<string> chain = new();
        ILogical? cursor = control.GetLogicalParent();
        int depth = 0;
        while (cursor is not null && depth < 6)
        {
            if (cursor is Control c)
            {
                chain.Add(BuildControlLabel(c));
            }

            cursor = cursor.LogicalParent;
            depth++;
        }

        if (chain.Count == 0)
        {
            return null;
        }

        chain.Reverse();
        return string.Join(" > ", chain);
    }

    /// <summary>
    /// Appends a short state summary for selector / input controls — whichever
    /// piece of runtime state is most likely to pinpoint the bad value that
    /// triggered the binding error. Silent for control types we don't recognise.
    /// </summary>
    private static string? BuildControlStateSummary(Control control)
    {
        return control switch
        {
            ComboBox cb => $"SelectedIndex={cb.SelectedIndex}, SelectedItem={Describe(cb.SelectedItem)}",
            ListBox lb => $"SelectedIndex={lb.SelectedIndex}, SelectedItem={Describe(lb.SelectedItem)}",
            TextBox tb => $"Text={Describe(tb.Text)}",
            TabItem ti => $"Header={Describe(ti.Header)}",
            TreeViewItem t => $"Header={Describe(t.Header)}",
            var _ => null,
        };

        static string Describe(object? value)
        {
            if (value is null)
            {
                return "(null)";
            }

            string text = value.ToString() ?? "(null)";
            // reduced cap from 80 → 30 chars.  This is the
            // last leg of a binding-validation error log line that fires
            // when a user types into a TextBox / ComboBox bound to a
            // typed property and Avalonia's coercion rejects the input.
            // The cap reduces the size of any incidental leak (e.g. the
            // user pasting a secret-bearing string into a number-only
            // NumericUpDown, which would otherwise dump up to 80 chars
            // of that string into the rolling log).  Even at 30 chars
            // the diagnostic stays useful — enough to recognise common
            // bad-input shapes ("Bash(*)", "32k", "true/false typo"),
            // which is what this logger exists for.
            return text.Length > 30 ? text[..30] + "…" : text;
        }
    }
}
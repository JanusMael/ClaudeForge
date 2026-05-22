using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml.Templates;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Converters;

/// <summary>
/// Identity converter for the source-to-target direction (<see cref="Convert"/>
/// returns the value unchanged), but <see cref="ConvertBack"/> intercepts
/// <c>null</c> target values and returns
/// <see cref="BindingOperations.DoNothing"/> so the binding engine
/// <em>skips</em> the write back to the source.
/// <para>
/// <strong>Why this exists:</strong> Avalonia's <c>ComboBox</c> reuses a single
/// <see cref="global::Avalonia.Controls.ContentControl"/> /
/// <c>SelectingItemsControl</c> instance across
/// <see cref="DataTemplate"/> matches. When
/// <c>ContentPresenter</c> swaps the <c>DataContext</c> from one
/// view-model to another, a ComboBox's <c>ItemsSource</c> and
/// <c>SelectedItem</c> bindings do not update atomically.
/// <c>SelectionModel.Update</c> briefly sees the old selection missing from
/// the new items collection and clears <c>SelectedItem</c> to <c>null</c>.
/// With a default TwoWay binding, that transient <c>null</c> flows back
/// through <c>BindingExpression.WriteValueToSource</c>, fails coercion when
/// the source property is a non-nullable type (typically an enum), and
/// publishes a <c>BindingErrorType.DataValidationError</c> — the
/// "adorner appears + red box + nothing useful logged" pattern.
/// </para>
/// <para>
/// Attaching this converter turns that transient null into a no-op: the source
/// property keeps its current value, no exception is raised, and the ComboBox
/// picks up the correct new selection as soon as the <c>ItemsSource</c> binding
/// catches up.
/// </para>
/// <para>
/// <strong>Scope of behavioural change:</strong> genuine user-initiated
/// selection clears (e.g. the user pressing Delete in a ComboBox that supports
/// it) are also suppressed from propagating to the source — by design. If a
/// binding target genuinely needs to represent "no selection", its source
/// property should be nullable and this converter should not be used.
/// </para>
/// </summary>
public sealed class SuppressNullWriteConverter : IValueConverter
{
    /// <summary>Reusable singleton — the converter has no per-instance state.</summary>
    public static readonly SuppressNullWriteConverter Instance = new();

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is null ? BindingOperations.DoNothing : value;
    }
}
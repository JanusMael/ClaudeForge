using System.Globalization;
using Avalonia.Data;
using Bennewitz.Ninja.LayeredEditors.Avalonia.Converters;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.Converters;

/// <summary>
/// Pins the contract of <see cref="SuppressNullWriteConverter"/>: identity in
/// the Convert direction, and <see cref="BindingOperations.DoNothing"/> for
/// null inputs in the ConvertBack direction. This is what stops the ComboBox
/// cross-product-scope-bleed InvalidCastException in
/// <c>SettingsGroupEditorView</c>.
/// </summary>
[TestClass]
public sealed class SuppressNullWriteConverterTests
{
    private static object? Convert(object? value)
    {
        return SuppressNullWriteConverter.Instance.Convert(
            value, typeof(object), parameter: null, CultureInfo.InvariantCulture);
    }

    private static object? ConvertBack(object? value)
    {
        return SuppressNullWriteConverter.Instance.ConvertBack(
            value, typeof(object), parameter: null, CultureInfo.InvariantCulture);
    }

    // -------------------------------------------------------------------
    // Convert — pure identity in every case.
    // -------------------------------------------------------------------

    [TestMethod]
    public void Convert_PassesNullThrough()
    {
        Assert.IsNull(Convert(null));
    }

    [TestMethod]
    public void Convert_PassesEnumValueThrough()
    {
        // StringComparison is a convenient system enum that needs no extra reference.
        Assert.AreEqual(StringComparison.Ordinal, Convert(StringComparison.Ordinal));
    }

    [TestMethod]
    public void Convert_PassesStringThrough()
    {
        Assert.AreEqual("hello", Convert("hello"));
    }

    // -------------------------------------------------------------------
    // ConvertBack — null ⇒ BindingOperations.DoNothing, else identity.
    // -------------------------------------------------------------------

    [TestMethod]
    public void ConvertBack_NullTargetYieldsDoNothingSentinel()
    {
        Assert.AreSame(BindingOperations.DoNothing, ConvertBack(null));
    }

    [TestMethod]
    public void ConvertBack_NonNullTargetReturnsSameInstance()
    {
        StringComparison value = StringComparison.CurrentCulture;
        Assert.AreEqual(value, ConvertBack(value));
    }

    [TestMethod]
    public void ConvertBack_ReferenceTypeReturnedUnchanged()
    {
        object value = new();
        Assert.AreSame(value, ConvertBack(value));
    }

    [TestMethod]
    public void Instance_IsSingleton()
    {
        Assert.AreSame(SuppressNullWriteConverter.Instance, SuppressNullWriteConverter.Instance);
    }
}
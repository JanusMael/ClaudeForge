using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels;

/// <summary>
/// One product the user can include in a backup — a checkbox row on the Backup tab.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the Backup page's two fixed <c>CheckBox</c> elements and the
/// <c>IncludeClaudeCode</c> / <c>IncludeClaudeDesktop</c> pair behind them. Those meant the
/// view enumerated products in markup, so a third product needed an AXAML edit, a third
/// observable property, and a third branch where the request was built.
/// </para>
/// <para>
/// Public, with public bound members, because the view binds to it by reflection.
/// </para>
/// </remarks>
public sealed partial class BackupProductViewModel : ObservableObject
{
    internal BackupProductViewModel(ProductDescriptor product, string displayName, bool isSelected)
    {
        Product = product;
        DisplayName = displayName;
        _isSelected = isSelected;
    }

    /// <summary>The product this row includes or excludes.</summary>
    internal ProductDescriptor Product { get; }

    /// <summary>
    /// Localized checkbox label. Resolved once at construction from the resource table —
    /// <see cref="ProductDescriptor.DisplayName"/> is not translated, and this text is.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>Whether this product is included in the next backup. Two-way bound.</summary>
    [ObservableProperty] private bool _isSelected;
}

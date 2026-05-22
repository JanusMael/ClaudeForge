using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using Bennewitz.Ninja.ClaudeForge.Converters;
using Bennewitz.Ninja.ClaudeForge.Core.Settings;
using Bennewitz.Ninja.ClaudeForge.Localization;

namespace Bennewitz.Ninja.ClaudeForge.Controls;

/// <summary>
/// Reusable 4-row scope legend reminding the user what each
/// <see cref="ConfigScope"/> value means: which file it maps to, what
/// priority it has, and a one-line semantic description.
/// <para>
/// Rendered in the WelcomeView (always-visible reference card), the
/// SettingsGroupEditorView "?" popup (per-editor lookup), and the
/// BackupRestoreView header (so users on the Backup page understand what
/// scope-of-config the backup covers without leaving the page).
/// </para>
/// <para>
/// Rows are pre-computed once in the constructor so compiled XAML bindings
/// stay reflection-free — the AXAML <c>ItemsControl</c> binds to
/// <see cref="Items"/> via <c>{Binding #LegendRoot.Items}</c>.
/// </para>
/// </summary>
public partial class ScopeLegend : UserControl
{
    /// <summary>
    /// Static 4-row dataset rendered by the AXAML <c>ItemsControl</c>. Order
    /// follows the <see cref="ConfigScope"/> priority chain (highest priority
    /// first): Managed > Local > Project > User.
    /// </summary>
    public IReadOnlyList<ScopeLegendRow> Items { get; }

    public ScopeLegend()
    {
        Items = BuildRows();
        InitializeComponent();
    }

    /// <summary>
    /// Builds the 4 row records from the existing <see cref="ConfigScope"/>
    /// enum + the existing <see cref="ScopeToBrushConverter"/> palette
    /// (no duplication of colour definitions).
    /// </summary>
    private static IReadOnlyList<ScopeLegendRow> BuildRows()
    {
        return
        [
            Build(ConfigScope.Managed, Strings.TextScopePathManaged, Strings.TextScopeMeaningManaged),
            Build(ConfigScope.Local, Strings.TextScopePathLocal, Strings.TextScopeMeaningLocal),
            Build(ConfigScope.Project, Strings.TextScopePathProject, Strings.TextScopeMeaningProject),
            Build(ConfigScope.User, Strings.TextScopePathUser, Strings.TextScopeMeaningUser),
        ];
    }

    private static ScopeLegendRow Build(ConfigScope scope, string path, string meaning)
    {
        IBrush? brush = new ScopeToBrushConverter()
            .Convert(scope, typeof(IBrush), null, CultureInfo.InvariantCulture) as IBrush;
        return new ScopeLegendRow(
            Name: ScopeToDisplayNameConverter.DisplayFor(scope),
            Path: path,
            Meaning: meaning,
            BadgeBrush: brush ?? Brushes.Gray);
    }
}

/// <summary>
/// Row record consumed by the AXAML <c>ItemsControl</c> in
/// <see cref="ScopeLegend"/>. Pure data; no notifications — rows are
/// constructed once and never mutated.
/// </summary>
/// <param name="Name">Scope name as shown in the badge pill and the name column.</param>
/// <param name="Path">File path with leading <c>~/</c> or <c>&lt;project&gt;/</c> placeholder.</param>
/// <param name="Meaning">One-sentence description of when/why this scope is written.</param>
/// <param name="BadgeBrush">Solid brush from <c>ScopeTheme.axaml</c> (red/blue/green/amber).</param>
public sealed record ScopeLegendRow(
    string Name,
    string Path,
    string Meaning,
    IBrush BadgeBrush);
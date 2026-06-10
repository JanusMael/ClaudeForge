using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Styling;

namespace Bennewitz.Ninja.ClaudeForge.Avalonia.Behaviors;

/// <summary>
/// Attached property that renders a string with <c>`backtick`</c> code spans into
/// a <see cref="TextBlock"/>'s inlines: prose segments render normally, code
/// segments render monospace + a theme-aware accent so syntax tokens (paths,
/// <c>mcp__server</c>, glob patterns, rule strings) stand out from the prose.
/// </summary>
/// <remarks>
/// Avalonia has no built-in markup-to-inlines binding. This keeps localized
/// strings whole (formatting lives in the backticks, not in fractured resx
/// fragments) and is reusable from XAML via
/// <c>behaviors:CodeInline.Markup="{Binding …}"</c> or <c>{x:Static …}</c>.
/// A string with no backticks renders as a single prose run, so unmarked or
/// not-yet-translated strings still display correctly.
/// </remarks>
public static class CodeInline
{
    private static readonly FontFamily MonoFont =
        new("Cascadia Mono,Cascadia Code,Consolas,Menlo,monospace");

    /// <summary>The backtick-markup source string for the target TextBlock.</summary>
    public static readonly AttachedProperty<string?> MarkupProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, string?>(
            "Markup", typeof(CodeInline));

    static CodeInline()
    {
        MarkupProperty.Changed.AddClassHandler<TextBlock>(
            (tb, e) => Apply(tb, e.GetNewValue<string?>()));
    }

    public static void SetMarkup(TextBlock target, string? value) =>
        target.SetValue(MarkupProperty, value);

    public static string? GetMarkup(TextBlock target) =>
        target.GetValue(MarkupProperty);

    private static void Apply(TextBlock target, string? markup)
    {
        target.Inlines ??= new InlineCollection();
        target.Inlines.Clear();
        if (string.IsNullOrEmpty(markup))
        {
            return;
        }

        IBrush codeBrush = CodeBrush();
        string[] segments = markup.Split('`');
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i].Length == 0)
            {
                continue;
            }

            Run run = new(segments[i]);
            if ((i % 2) == 1)
            {
                run.FontFamily = MonoFont;
                run.FontWeight = FontWeight.SemiBold;
                run.Foreground = codeBrush;
            }

            target.Inlines.Add(run);
        }
    }

    /// <summary>
    /// Theme-aware accent for code tokens: a lighter blue on dark backgrounds, a
    /// darker blue on light, so tokens stay legible (WCAG AA) in both themes
    /// without depending on a host-specific resource key.
    /// </summary>
    private static IBrush CodeBrush()
    {
        bool dark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        return new SolidColorBrush(Color.Parse(dark ? "#4FC1FF" : "#0B5FB0"));
    }
}

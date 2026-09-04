namespace Bennewitz.Ninja.AgentForge.Avalonia.Shell.Essentials;

/// <summary>
/// One choice on an <see cref="EssentialsCardKind.LabelledEnum"/> card: the token the host's
/// read/write delegates understand, plus the words shown to the user.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b><see cref="Value"/> is opaque to the shell.</b> It is a discriminator the host hands back
/// to itself — for <c>autoupdate</c> it names an <c>OpenCodeAutoupdateMode</c>, and the delegate
/// turns that into the JSON <see langword="true"/>, <see langword="false"/> or <c>"notify"</c> the
/// file actually holds. That indirection is the whole reason this kind exists rather than reusing
/// <see cref="EssentialsCardKind.EnumString"/>, whose ComboBox commits and DISPLAYS the same
/// string: a picker offering <c>true</c> / <c>false</c> / <c>notify</c> would contradict the full
/// editor for the same key, which says "Automatic" / "Disabled" / "Notify".
/// </para>
/// <para>
/// ⚠ <b>Deliberately not <see cref="ModelSuggestionItem"/></b>, despite the near-identical shape —
/// and the difference is exactly what <see cref="ToString"/> must return. That record backs a
/// free-form AutoCompleteBox, so its <c>ToString</c> is the <i>value</i> the control commits on
/// typed text. This one backs a closed ComboBox, where nothing is committed by typing and
/// <c>ToString</c> is only ever read by the automation peer.
/// </para>
/// </remarks>
/// <param name="Value">The host's discriminator, round-tripped through its read/write delegates.</param>
/// <param name="Label">Short text shown in the picker.</param>
/// <param name="Description">
/// Optional dim subtitle saying what choosing this writes. Shown under the label.
/// </param>
public sealed record EssentialsEnumOption(string Value, string Label, string? Description = null)
{
    /// <summary>True when <see cref="Description"/> has something to show.</summary>
    public bool HasDescription => !string.IsNullOrEmpty(Description);

    /// <summary>What a screen reader announces for this row.</summary>
    /// <remarks>
    /// ⛔⛔ <b>A <c>ComboBox</c> bound to <c>ItemsSource</c> names its generated
    /// <c>ComboBoxItem</c>s from the ITEM, not from the <c>ItemTemplate</c></b> — and a record's
    /// synthesized <c>ToString</c> would announce
    /// <c>EssentialsEnumOption { Value = Notify, Label = Notify only, … }</c>. This repo has
    /// measured that class of defect three times across three container types, including one case
    /// where a template with a single bound <c>TextBlock</c> root still failed to name its
    /// container. Overriding here rather than templating around it is the fix that actually holds.
    /// <para>
    /// The LABEL, never the <see cref="Value"/>: the value is an internal discriminator
    /// (<c>"Notify"</c>, <c>"NotSet"</c>) that means nothing to the person listening.
    /// </para>
    /// </remarks>
    public override string ToString() => Label;
}

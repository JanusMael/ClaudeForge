using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Essentials;
using Bennewitz.Ninja.ScopedEditors.Abstractions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.ViewModels;

/// <summary>
/// The kind/payload invariants the card constructor enforces, and the one member a picker's
/// automation peer reads.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>Shell types, tested from this project because the shell has no test project of its own</b>
/// — the same reason <see cref="EssentialsCardOptionsTests"/> lives here. The two kinds under test
/// exist for the OTHER app, so the alternative was a guard sitting in the consumer's project while
/// the mechanism sits in the shell.
/// </para>
/// <para>
/// ⭐ Every failure this file describes is <b>invisible at runtime</b>: a derived card with a
/// writer looks editable and persists nothing; an editable card without one drops every edit; a
/// labelled picker with no options reads as "this setting has no choices". None of them throw,
/// none of them log, and all of them render.
/// </para>
/// </remarks>
public sealed class EssentialsCardKindTests
{
    private static EssentialsCardOptions Options(
        EssentialsCardKind kind,
        bool withWriter,
        IReadOnlyList<EssentialsEnumOption>? labelled = null) => new()
        {
            Id = "probe",
            Title = "t",
            Body = "b",
            Severity = AppSeverity.Neutral,
            Kind = kind,
            ReadAsync = _ => Task.CompletedTask,
            WriteAsync = withWriter ? _ => Task.CompletedTask : null,
            LabelledOptions = labelled,
        };

    [Fact]
    public void DerivedCard_WithAWriter_IsRejected()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => new EssentialsCardViewModel(Options(EssentialsCardKind.Derived, withWriter: true)));

        Assert.Contains("Derived", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(EssentialsCardKind.Bool)]
    [InlineData(EssentialsCardKind.Int)]
    [InlineData(EssentialsCardKind.EnumString)]
    [InlineData(EssentialsCardKind.StringList)]
    public void EditableCard_WithoutAWriter_IsRejected(EssentialsCardKind kind)
    {
        Assert.Throws<ArgumentException>(
            () => new EssentialsCardViewModel(Options(kind, withWriter: false)));
    }

    /// <summary>The labelled kind is covered separately because it also needs its options.</summary>
    [Fact]
    public void LabelledEnumCard_WithoutAWriter_IsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => new EssentialsCardViewModel(
                Options(EssentialsCardKind.LabelledEnum, withWriter: false, [new("v", "l")])));
    }

    [Fact]
    public void LabelledEnumCard_WithNoOptions_IsRejected()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => new EssentialsCardViewModel(
                Options(EssentialsCardKind.LabelledEnum, withWriter: true, [])));

        Assert.Contains("at least one option", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonLabelledCard_CarryingOptions_IsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => new EssentialsCardViewModel(
                Options(EssentialsCardKind.Bool, withWriter: true, [new("v", "l")])));
    }

    /// <summary>
    /// A derived card's write path completes rather than dereferencing the writer it does not have.
    /// </summary>
    /// <remarks>
    /// The call is reachable: <c>StringListValues.CollectionChanged</c> and every value-changed
    /// partial fire <c>_ = WriteAsync()</c>, and a future card kind could set a value on a derived
    /// card. Fire-and-forget means a throw here would be an unobserved task exception, surfacing
    /// far from its cause or not at all.
    /// </remarks>
    [Fact]
    public async Task DerivedCard_WriteAsync_CompletesWithoutAWriter()
    {
        EssentialsCardViewModel card = new(Options(EssentialsCardKind.Derived, withWriter: false));

        await card.WriteAsync();

        Assert.Equal(EssentialsCardKind.Derived, card.Kind);
    }

    [Fact]
    public void DerivedCard_HasNoLabelledOptions_AndAnEmptyDerivedText()
    {
        EssentialsCardViewModel card = new(Options(EssentialsCardKind.Derived, withWriter: false));

        Assert.Empty(card.LabelledOptions);
        Assert.Equal(string.Empty, card.DerivedText);
    }

    /// <summary>
    /// Selecting an option routes through the writer, exactly as the other three kinds' setters do.
    /// </summary>
    [Fact]
    public void SelectingAnOption_Writes()
    {
        int writes = 0;
        EssentialsEnumOption chosen = new("Notify", "Notify only");
        EssentialsCardViewModel card = new(new EssentialsCardOptions
        {
            Id = "probe",
            Title = "t",
            Body = "b",
            Severity = AppSeverity.Neutral,
            Kind = EssentialsCardKind.LabelledEnum,
            LabelledOptions = [chosen],
            ReadAsync = _ => Task.CompletedTask,
            WriteAsync = _ =>
            {
                writes++;
                return Task.CompletedTask;
            },
        });

        MessageAssert.Equal(0, writes, "Construction must not write.");

        card.SelectedOption = chosen;

        Assert.Equal(1, writes);
    }

    /// <summary>A selection made while loading is a read, not an edit, and must not write.</summary>
    [Fact]
    public void SelectingAnOption_WhileLoading_DoesNotWrite()
    {
        int writes = 0;
        EssentialsEnumOption chosen = new("Notify", "Notify only");
        EssentialsCardViewModel card = new(new EssentialsCardOptions
        {
            Id = "probe",
            Title = "t",
            Body = "b",
            Severity = AppSeverity.Neutral,
            Kind = EssentialsCardKind.LabelledEnum,
            LabelledOptions = [chosen],
            ReadAsync = _ => Task.CompletedTask,
            WriteAsync = _ =>
            {
                writes++;
                return Task.CompletedTask;
            },
        });

        card.IsLoading = true;
        card.SelectedOption = chosen;
        card.IsLoading = false;

        Assert.Equal(0, writes);
    }

    /// <summary>
    /// The danger predicate re-runs on a selection change, so a card whose unsafe state IS a
    /// chosen option raises its banner without waiting for a reload.
    /// </summary>
    [Fact]
    public void SelectingAnOption_RecomputesDanger()
    {
        EssentialsEnumOption safe = new("safe", "Safe");
        EssentialsEnumOption unsafeOption = new("unsafe", "Unsafe");
        EssentialsCardViewModel card = new(new EssentialsCardOptions
        {
            Id = "probe",
            Title = "t",
            Body = "b",
            Severity = AppSeverity.Critical,
            Kind = EssentialsCardKind.LabelledEnum,
            LabelledOptions = [safe, unsafeOption],
            ReadAsync = _ => Task.CompletedTask,
            WriteAsync = _ => Task.CompletedTask,
            IsDangerPredicate = c => c.SelectedOption?.Value == "unsafe",
        });

        Assert.False(card.IsDanger);

        card.SelectedOption = unsafeOption;
        Assert.True(card.IsDanger);

        card.SelectedOption = safe;
        Assert.False(card.IsDanger);
    }

    /// <summary>
    /// The option's <c>ToString</c> is the LABEL.
    /// </summary>
    /// <remarks>
    /// ⛔⛔ <b>This is the accessible name of a generated <c>ComboBoxItem</c>, not a debugging
    /// nicety.</b> A ComboBox bound to <c>ItemsSource</c> names its containers from the item, never
    /// from the <c>ItemTemplate</c> — measured in this repo across three container types — so
    /// without the override every row of the picker announces
    /// <c>EssentialsEnumOption { Value = …, Label = …, Description = … }</c>. Asserting the value
    /// is NOT returned matters just as much: <c>"NotSet"</c> is an internal discriminator.
    /// </remarks>
    [Fact]
    public void OptionToString_IsTheLabel_NotTheValue()
    {
        EssentialsEnumOption option = new("NotSet", "Not set", "Writes nothing.");

        Assert.Equal("Not set", option.ToString());
    }

    [Fact]
    public void OptionHasDescription_TracksTheDescription()
    {
        Assert.False(new EssentialsEnumOption("v", "l").HasDescription);
        Assert.False(new EssentialsEnumOption("v", "l", string.Empty).HasDescription);
        Assert.True(new EssentialsEnumOption("v", "l", "d").HasDescription);
    }
}

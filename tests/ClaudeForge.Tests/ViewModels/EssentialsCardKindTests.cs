using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Essentials;
using Bennewitz.Ninja.LayeredEditors.Abstractions;

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
[TestClass]
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

    [TestMethod]
    public void DerivedCard_WithAWriter_IsRejected()
    {
        ArgumentException ex = Assert.ThrowsExactly<ArgumentException>(
            () => new EssentialsCardViewModel(Options(EssentialsCardKind.Derived, withWriter: true)));

        StringAssert.Contains(ex.Message, "Derived", StringComparison.Ordinal);
    }

    [TestMethod]
    [DataRow(EssentialsCardKind.Bool)]
    [DataRow(EssentialsCardKind.Int)]
    [DataRow(EssentialsCardKind.EnumString)]
    [DataRow(EssentialsCardKind.StringList)]
    public void EditableCard_WithoutAWriter_IsRejected(EssentialsCardKind kind)
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new EssentialsCardViewModel(Options(kind, withWriter: false)));
    }

    /// <summary>The labelled kind is covered separately because it also needs its options.</summary>
    [TestMethod]
    public void LabelledEnumCard_WithoutAWriter_IsRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => new EssentialsCardViewModel(
                Options(EssentialsCardKind.LabelledEnum, withWriter: false, [new("v", "l")])));
    }

    [TestMethod]
    public void LabelledEnumCard_WithNoOptions_IsRejected()
    {
        ArgumentException ex = Assert.ThrowsExactly<ArgumentException>(
            () => new EssentialsCardViewModel(
                Options(EssentialsCardKind.LabelledEnum, withWriter: true, [])));

        StringAssert.Contains(ex.Message, "at least one option", StringComparison.Ordinal);
    }

    [TestMethod]
    public void NonLabelledCard_CarryingOptions_IsRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(
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
    [TestMethod]
    public async Task DerivedCard_WriteAsync_CompletesWithoutAWriter()
    {
        EssentialsCardViewModel card = new(Options(EssentialsCardKind.Derived, withWriter: false));

        await card.WriteAsync();

        Assert.AreEqual(EssentialsCardKind.Derived, card.Kind);
    }

    [TestMethod]
    public void DerivedCard_HasNoLabelledOptions_AndAnEmptyDerivedText()
    {
        EssentialsCardViewModel card = new(Options(EssentialsCardKind.Derived, withWriter: false));

        Assert.AreEqual(0, card.LabelledOptions.Count);
        Assert.AreEqual(string.Empty, card.DerivedText);
    }

    /// <summary>
    /// Selecting an option routes through the writer, exactly as the other three kinds' setters do.
    /// </summary>
    [TestMethod]
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

        Assert.AreEqual(0, writes, "Construction must not write.");

        card.SelectedOption = chosen;

        Assert.AreEqual(1, writes);
    }

    /// <summary>A selection made while loading is a read, not an edit, and must not write.</summary>
    [TestMethod]
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

        Assert.AreEqual(0, writes);
    }

    /// <summary>
    /// The danger predicate re-runs on a selection change, so a card whose unsafe state IS a
    /// chosen option raises its banner without waiting for a reload.
    /// </summary>
    [TestMethod]
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

        Assert.IsFalse(card.IsDanger);

        card.SelectedOption = unsafeOption;
        Assert.IsTrue(card.IsDanger);

        card.SelectedOption = safe;
        Assert.IsFalse(card.IsDanger);
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
    [TestMethod]
    public void OptionToString_IsTheLabel_NotTheValue()
    {
        EssentialsEnumOption option = new("NotSet", "Not set", "Writes nothing.");

        Assert.AreEqual("Not set", option.ToString());
    }

    [TestMethod]
    public void OptionHasDescription_TracksTheDescription()
    {
        Assert.IsFalse(new EssentialsEnumOption("v", "l").HasDescription);
        Assert.IsFalse(new EssentialsEnumOption("v", "l", string.Empty).HasDescription);
        Assert.IsTrue(new EssentialsEnumOption("v", "l", "d").HasDescription);
    }
}

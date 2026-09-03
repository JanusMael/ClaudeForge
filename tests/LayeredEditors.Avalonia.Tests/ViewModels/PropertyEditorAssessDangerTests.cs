using Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.Fakes;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Tests.ViewModels;

/// <summary>
/// <see cref="IDangerAnnotatedEditor.AssessDanger(string)"/> on
/// <see cref="PropertyEditorViewModel"/>: which editor answers for a path, and what a caller can
/// tell from the answer.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam that lets a second surface — search — show a severity without reclassifying.
/// The two failure modes it has to rule out are both silent:
/// </para>
/// <para>
/// ⛔ <b>Returning <see cref="DangerAssessment.Unremarkable"/> for a path the editor does not
/// own</b> would make a caller's walk stop at the first editor it asks and report every later
/// path as safe. Hence the null-vs-Unremarkable assertions below, which look pedantic and are
/// not.
/// </para>
/// <para>
/// ⛔ <b>Answering a nested path from the PARENT's value</b> would put a dot on the right row with
/// the wrong basis: the parent object's value is not the child's, so <c>IsDangerNow</c> would be
/// decided by the wrong data. The nested tests assert the child's value reached the classifier.
/// </para>
/// </remarks>
[TestClass]
public sealed class PropertyEditorAssessDangerTests
{
    /// <summary>Classifies from the value it is handed, so a test can prove WHOSE value arrived.</summary>
    private sealed class ValueEchoClassifier : IDangerClassifier
    {
        public IReadOnlyCollection<string> ClassifiedPaths { get; init; } = [];

        public DangerAssessment Classify(string path, IEditorScope? scope, object? currentValue) =>
            new(AppSeverity.Critical, currentValue is "unsafe", $"{path} held '{currentValue}'");
    }

    private sealed class StubEditor : PropertyEditorViewModel
    {
        private string? _value;

        public StubEditor(string path, IEditorScope scope) : base(new FakeEditorSchema(path), scope) { }

        public void SetValue(string? value)
        {
            _value = value;
            TrackValueSet(!string.IsNullOrEmpty(value));
        }

        public override object? ToValue() => _value;

        public override void LoadFromValue(IEditorValue value, IEditorScope editingScope) { }

        protected override void OnResetToInherited() => _value = null;
    }

    /// <summary>
    /// An object editor that is NOT the library's own <c>ObjectPropertyEditorViewModel</c> — it
    /// only implements <see cref="IChildEditorHost"/>, exactly like the app-side object editor
    /// that interface exists to cover. A descent written as a type test would miss this.
    /// </summary>
    private sealed class ForeignObjectEditor : PropertyEditorViewModel, IChildEditorHost
    {
        public ForeignObjectEditor(string path, IEditorScope scope, params PropertyEditorViewModel[] children)
            : base(new FakeEditorSchema(path), scope) => Children = children;

        public IReadOnlyList<PropertyEditorViewModel> Children { get; }

        public override object? ToValue() => null;

        public override void LoadFromValue(IEditorValue value, IEditorScope editingScope) { }

        protected override void OnResetToInherited() { }
    }

    private static FakeEditorScope Scope() => new(0, "Global", "Global");

    private static StubEditor Attached(string path, string? value = null)
    {
        StubEditor editor = new(path, Scope());
        editor.AttachDangerClassifier(new ValueEchoClassifier());
        editor.SetValue(value);
        return editor;
    }

    // ── Who answers ───────────────────────────────────────────────────────────

    [TestMethod]
    public void OwnPathReturnsOwnAssessment()
    {
        StubEditor editor = Attached("share", "unsafe");

        DangerAssessment? assessment = editor.AssessDanger("share");

        Assert.IsNotNull(assessment, "An editor must answer for its own path.");
        Assert.AreEqual("share held 'unsafe'", assessment.Explanation);
        Assert.IsTrue(assessment.IsDangerNow, "The editor's own current value must decide IsDangerNow.");
    }

    [TestMethod]
    public void ForeignPathReturnsNullRatherThanUnremarkable()
    {
        StubEditor editor = Attached("share", "unsafe");

        Assert.IsNull(editor.AssessDanger("snapshot"),
            "A path this editor does not render must come back null, NOT Unremarkable. Both render "
            + "as no dot, but only null tells a caller walking a list of editors to keep looking — "
            + "with Unremarkable the walk stops at the first editor and calls everything else safe.");
    }

    [TestMethod]
    public void PathMatchIsOrdinalNotCaseInsensitive()
    {
        StubEditor editor = Attached("permission", "unsafe");

        Assert.IsNull(editor.AssessDanger("Permission"),
            "These are JSON paths, not display text. A case-insensitive match would let one key "
            + "answer for a differently-cased sibling, and the danger table's own lookup is ordinal.");
    }

    [TestMethod]
    public void AnEditorWithNoClassifierStillOwnsItsPath()
    {
        StubEditor editor = new("share", Scope());

        DangerAssessment? assessment = editor.AssessDanger("share");

        Assert.IsNotNull(assessment,
            "Ownership is about which editor renders the path, not whether the product triaged it. "
            + "Returning null here would make a product with no danger table indistinguishable "
            + "from a path no editor renders.");
        Assert.AreEqual(DangerAssessment.Unremarkable, assessment);
    }

    // ── Descent into children ─────────────────────────────────────────────────

    [TestMethod]
    public void NestedPathIsAnsweredByTheChildThatRendersIt()
    {
        StubEditor child = Attached("attachment.image.maxWidth", "unsafe");
        ForeignObjectEditor parent = new("attachment", Scope(), child);
        parent.AttachDangerClassifier(new ValueEchoClassifier());

        DangerAssessment? assessment = parent.AssessDanger("attachment.image.maxWidth");

        Assert.IsNotNull(assessment, "The descent must find a child's path through IChildEditorHost.");
        Assert.AreEqual("attachment.image.maxWidth held 'unsafe'", assessment.Explanation,
            "The CHILD's assessment must come back — carrying the child's own value. Answering "
            + "from the parent would decide IsDangerNow from the wrong data.");
    }

    [TestMethod]
    public void DescentReachesAChildEditorHostThatIsNotTheLibrarysOwnObjectEditor()
    {
        // ForeignObjectEditor deliberately does not derive from ObjectPropertyEditorViewModel.
        // There are two of those classes — one in the library, one in the app — and the app's does
        // not derive from the library's, so a descent written as a type test covers only half the
        // object editors in play and silently reports nested keys as unremarkable.
        StubEditor grandchild = Attached("a.b.c", "unsafe");
        ForeignObjectEditor inner = new("a.b", Scope(), grandchild);
        ForeignObjectEditor outer = new("a", Scope(), inner);

        Assert.IsNotNull(outer.AssessDanger("a.b.c"),
            "The descent must be driven by the IChildEditorHost interface, not by a concrete "
            + "object-editor type, or the app's own object editors are skipped.");
    }

    [TestMethod]
    public void ForeignPathIsNullEvenWhenChildrenExist()
    {
        StubEditor child = Attached("attachment.image.maxWidth", "unsafe");
        ForeignObjectEditor parent = new("attachment", Scope(), child);

        Assert.IsNull(parent.AssessDanger("attachment.image.maxHeight"),
            "A miss below a parent that HAS children must still be null, or the walk stops early.");
    }
}

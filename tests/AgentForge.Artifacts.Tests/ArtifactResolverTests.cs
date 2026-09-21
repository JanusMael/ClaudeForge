namespace Bennewitz.Ninja.AgentForge.Artifacts.Tests;

/// <summary>
/// A source whose entries are handed to it, so ordering can be proved without a filesystem.
/// </summary>
internal sealed class FakeSource(
    string id,
    ArtifactKind kind,
    ArtifactScope scope,
    params string[] names) : IArtifactSource
{
    private readonly List<ArtifactRef> _entries =
        [.. names.Select(n => new ArtifactRef(n, kind, scope, ArtifactForm.File, $"/{id}/{n}", id))];

    public string Id { get; } = id;

    public ArtifactKind Kind { get; } = kind;

    public ArtifactScope Scope { get; } = scope;

    /// <summary>Set to throw from <see cref="Enumerate"/> mid-sequence.</summary>
    public Exception? ThrowAfterFirst { get; init; }

    public IEnumerable<ArtifactRef> Enumerate()
    {
        // ⚠ Deliberately LAZY, like every real directory walk: the exception surfaces while the
        // caller iterates, not when Enumerate() is called. A resolver that wrapped the bare call
        // in a try would catch nothing.
        foreach (ArtifactRef e in _entries)
        {
            yield return e;

            if (ThrowAfterFirst is { } ex)
            {
                throw ex;
            }
        }
    }

    /// <summary>Add an entry in a specific form, for the inline-versus-file cases.</summary>
    public FakeSource With(string name, ArtifactForm form, string? location = null)
    {
        _entries.Add(new ArtifactRef(name, Kind, Scope, form, location ?? $"/{Id}/{name}", Id));
        return this;
    }
}

/// <summary>
/// Resolution: grouping declarations of one name into one precedence-ordered chain.
/// </summary>
/// <remarks>
/// This is the type that decides which agent actually runs, so the tests are about <b>order</b> and
/// about <b>what survives</b> — never about merging, which is deliberately not this type's job.
/// </remarks>
[TestClass]
public sealed class ArtifactResolverTests
{
    private static readonly ArtifactScope BuiltIn = new("builtin", "Built-in", 0);
    private static readonly ArtifactScope Global = new("global", "Global", 10);
    private static readonly ArtifactScope Project = new("project", "Project", 20);

    private static ResolvedArtifact One(params IArtifactSource[] sources) =>
        ArtifactResolver.Resolve(sources).Single();

    // ── The empty and trivial cases ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void NoSourcesResolveToNothing()
    {
        Assert.AreEqual(0, ArtifactResolver.Resolve([]).Count);
    }

    [TestMethod]
    public void ASingleDeclarationIsNotShadowed()
    {
        ResolvedArtifact a = One(new FakeSource("g", ArtifactKind.Agent, Global, "build"));

        Assert.AreEqual("build", a.Name);
        Assert.AreEqual(ArtifactKind.Agent, a.Kind);
        Assert.IsFalse(a.IsShadowed);
        Assert.AreEqual(0, a.Shadowed.Count());
        Assert.AreEqual("g", a.Effective.SourceId);
    }

    [TestMethod]
    public void ResolveRejectsANullSourceList()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => ArtifactResolver.Resolve(null!));
    }

    // ── Precedence: the whole point ──────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Higher precedence first, whatever order the sources were listed in — otherwise the chain
    /// would report the winner as whichever source happened to be registered first.
    /// </summary>
    [TestMethod]
    public void TheChainIsOrderedByPrecedence_NotByListingOrder()
    {
        ResolvedArtifact a = One(
            new FakeSource("builtin", ArtifactKind.Agent, BuiltIn, "build"),
            new FakeSource("project", ArtifactKind.Agent, Project, "build"),
            new FakeSource("global", ArtifactKind.Agent, Global, "build"));

        CollectionAssert.AreEqual(
            new[] { "project", "global", "builtin" },
            a.Entries.Select(e => e.SourceId).ToArray());
        Assert.AreEqual("project", a.Effective.SourceId);
        CollectionAssert.AreEqual(
            new[] { "global", "builtin" },
            a.Shadowed.Select(e => e.SourceId).ToArray());
        Assert.IsTrue(a.IsShadowed);
    }

    /// <summary>
    /// The five-layer chain the plan names: an agent may be built in, in global JSON, in a global
    /// markdown file, in project JSON and in a project markdown file at once.
    /// </summary>
    [TestMethod]
    public void AFiveLayerChainKeepsEveryLayer()
    {
        ArtifactScope globalJson = new("global", "Global", 10);
        ArtifactScope globalFile = new("global", "Global", 11);
        ArtifactScope projectJson = new("project", "Project", 20);
        ArtifactScope projectFile = new("project", "Project", 21);

        ResolvedArtifact a = One(
            new FakeSource("builtin", ArtifactKind.Agent, BuiltIn, "build"),
            new FakeSource("global-json", ArtifactKind.Agent, globalJson, "build"),
            new FakeSource("global-md", ArtifactKind.Agent, globalFile, "build"),
            new FakeSource("project-json", ArtifactKind.Agent, projectJson, "build"),
            new FakeSource("project-md", ArtifactKind.Agent, projectFile, "build"));

        Assert.AreEqual(5, a.Entries.Count);
        CollectionAssert.AreEqual(
            new[] { "project-md", "project-json", "global-md", "global-json", "builtin" },
            a.Entries.Select(e => e.SourceId).ToArray());
    }

    /// <summary>
    /// ⚠ Ties keep listing order, and that is the only claim the resolver can honestly make about
    /// two declarations at the same precedence.
    /// </summary>
    [TestMethod]
    public void EqualPrecedenceKeepsListingOrder()
    {
        ResolvedArtifact a = One(
            new FakeSource("second", ArtifactKind.Skill, Global, "review"),
            new FakeSource("third", ArtifactKind.Skill, Global, "review"),
            new FakeSource("first", ArtifactKind.Skill, Project, "review"));

        CollectionAssert.AreEqual(
            new[] { "first", "second", "third" },
            a.Entries.Select(e => e.SourceId).ToArray());
    }

    /// <summary>
    /// A source may declare the same name twice — OpenCode's recursive skill discovery flattens
    /// nested directories into one namespace, so this is reachable rather than hypothetical.
    /// </summary>
    [TestMethod]
    public void OneSourceDeclaringANameTwiceContributesBothEntries()
    {
        FakeSource s = new FakeSource("g", ArtifactKind.Skill, Global, "review");
        s.With("review", ArtifactForm.File, "/g/nested/review");

        ResolvedArtifact a = One(s);

        Assert.AreEqual(2, a.Entries.Count);
        Assert.IsTrue(a.IsShadowed);
        CollectionAssert.AreEqual(
            new[] { "/g/review", "/g/nested/review" },
            a.Entries.Select(e => e.Location).ToArray());
    }

    // ── Grouping: what is and is not the same artifact ───────────────────────────────────────────

    /// <summary>
    /// ⚠ Grouping is per KIND as well as name. A skill and an agent both called <c>review</c> are
    /// two artifacts, not one shadowing the other.
    /// </summary>
    [TestMethod]
    public void TheSameNameInDifferentKindsIsTwoArtifacts()
    {
        IReadOnlyList<ResolvedArtifact> all = ArtifactResolver.Resolve([
            new FakeSource("a", ArtifactKind.Agent, Global, "review"),
            new FakeSource("s", ArtifactKind.Skill, Global, "review"),
        ]);

        Assert.AreEqual(2, all.Count);
        Assert.IsFalse(all[0].IsShadowed);
        Assert.IsFalse(all[1].IsShadowed);
        CollectionAssert.AreEquivalent(
            new[] { ArtifactKind.Agent, ArtifactKind.Skill },
            all.Select(a => a.Kind).ToArray());
    }

    /// <summary>
    /// ⚠⚠ <b>Names are compared Ordinal, and the reason is the filesystem.</b> Both products run on
    /// Linux and macOS as well as Windows. Folding case would claim <c>Build.md</c> and
    /// <c>build.md</c> are one artifact shadowing the other, when a case-sensitive filesystem hands
    /// the tool two independent files.
    /// </summary>
    [TestMethod]
    public void NamesAreCaseSensitive()
    {
        IReadOnlyList<ResolvedArtifact> all = ArtifactResolver.Resolve([
            new FakeSource("g", ArtifactKind.Agent, Global, "build"),
            new FakeSource("p", ArtifactKind.Agent, Project, "Build"),
        ]);

        Assert.AreEqual(2, all.Count, "'build' and 'Build' are different artifacts");
        Assert.IsFalse(all.Any(a => a.IsShadowed));
    }

    /// <summary>
    /// Results come out in the order each artifact was first seen, so a diff of the resolved set is
    /// reviewable rather than hash-ordered.
    /// </summary>
    [TestMethod]
    public void ResultOrderFollowsFirstAppearance()
    {
        IReadOnlyList<ResolvedArtifact> all = ArtifactResolver.Resolve([
            new FakeSource("g", ArtifactKind.Agent, Global, "zeta", "alpha"),
            new FakeSource("p", ArtifactKind.Agent, Project, "middle", "alpha"),
        ]);

        CollectionAssert.AreEqual(
            new[] { "zeta", "alpha", "middle" },
            all.Select(a => a.Name).ToArray());
    }

    // ── The form travels, because S7 says it must ────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>Spike S7: inline JSON and a markdown file of the same name DEEP-MERGE, file wins per
    /// field.</b> So the resolver must not collapse the chain — it has to keep both entries AND
    /// keep which form each came from, or a consumer cannot implement the merge and a UI would
    /// wrongly report the file as shadowing the inline definition.
    /// </summary>
    [TestMethod]
    public void AnInlineAndAFileDeclarationBothSurvive_WithTheirFormsIntact()
    {
        FakeSource inline = new("cfg", ArtifactKind.Agent, Global);
        inline.With("dup", ArtifactForm.Inline, "$.agent.dup");

        ResolvedArtifact a = One(
            inline,
            new FakeSource("md", ArtifactKind.Agent, Project, "dup"));

        Assert.AreEqual(2, a.Entries.Count);
        Assert.AreEqual(ArtifactForm.File, a.Entries[0].Form);
        Assert.AreEqual(ArtifactForm.Inline, a.Entries[1].Form);
        Assert.AreEqual("$.agent.dup", a.Entries[1].Location,
            "the inline declaration keeps its JSON path so a consumer can still read its fields");
    }

    /// <summary>
    /// A remote source is listed, never fetched — so it appears in the chain like any other
    /// declaration, carrying its URL.
    /// </summary>
    [TestMethod]
    public void ARemoteDeclarationIsListedWithItsUrl()
    {
        FakeSource remote = new("urls", ArtifactKind.Skill, Global);
        remote.With("shared", ArtifactForm.Remote, "https://example.invalid/skill.md");

        ResolvedArtifact a = One(remote);

        Assert.AreEqual(ArtifactForm.Remote, a.Effective.Form);
        Assert.AreEqual("https://example.invalid/skill.md", a.Effective.Location);
    }

    // ── Fail-soft: one bad source must not empty the page ────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ <b>A source that throws costs its own entries and nothing else.</b> The memory inventory
    /// has always promised "never throws on enumeration"; losing that here would turn one unreadable
    /// directory or denied ACL into an empty page.
    /// </summary>
    [TestMethod]
    public void AThrowingSourceDoesNotStopTheOthers()
    {
        FakeSource bad = new("bad", ArtifactKind.Agent, Project, "first", "never-reached")
        {
            ThrowAfterFirst = new UnauthorizedAccessException("denied"),
        };

        IReadOnlyList<ResolvedArtifact> all = ArtifactResolver.Resolve([
            bad,
            new FakeSource("good", ArtifactKind.Agent, Global, "survivor"),
        ]);

        CollectionAssert.AreEquivalent(
            new[] { "survivor" },
            all.Select(a => a.Name).ToArray(),
            "the throwing source contributes nothing, and its neighbour is unaffected");
    }

    /// <summary>
    /// ⚠ The throw happens DURING iteration, which is what a directory walk does. Pinned separately
    /// because a resolver that only wrapped the <c>Enumerate()</c> call would catch nothing and this
    /// is the test that would notice.
    /// </summary>
    [TestMethod]
    public void AnIoFailureMidEnumerationIsSwallowed()
    {
        FakeSource bad = new("bad", ArtifactKind.Rule, Global, "a", "b")
        {
            ThrowAfterFirst = new IOException("the disk went away"),
        };

        Assert.AreEqual(0, ArtifactResolver.Resolve([bad]).Count);
    }

    /// <summary>
    /// ⚠ An unexpected exception type is NOT swallowed. Fail-soft covers the environment being
    /// hostile — a missing directory, a denied ACL, an unsupported path — not a source with a bug in
    /// it, which should surface rather than silently produce an incomplete page.
    /// </summary>
    [TestMethod]
    public void AProgrammingErrorInASourceIsNotSwallowed()
    {
        FakeSource broken = new("broken", ArtifactKind.Agent, Global, "a", "b")
        {
            ThrowAfterFirst = new InvalidOperationException("bug"),
        };

        Assert.ThrowsExactly<InvalidOperationException>(
            () => ArtifactResolver.Resolve([broken]));
    }

    [TestMethod]
    public void ANullSourceInTheListIsSkipped()
    {
        IReadOnlyList<ResolvedArtifact> all = ArtifactResolver.Resolve([
            null!,
            new FakeSource("g", ArtifactKind.Agent, Global, "build"),
        ]);

        Assert.AreEqual(1, all.Count);
    }

    // ── The chain's own invariants ───────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>Effective</c> indexes <c>Entries[0]</c>, so an empty chain would be an
    /// <see cref="IndexOutOfRangeException"/> waiting to happen. The resolver never produces one —
    /// a name only exists because something declared it.
    /// </summary>
    [TestMethod]
    public void EveryResolvedArtifactHasAtLeastOneEntry()
    {
        IReadOnlyList<ResolvedArtifact> all = ArtifactResolver.Resolve([
            new FakeSource("g", ArtifactKind.Agent, Global, "a", "b"),
            new FakeSource("p", ArtifactKind.Skill, Project, "c"),
        ]);

        Assert.AreEqual(3, all.Count);
        Assert.IsTrue(all.All(a => a.Entries.Count >= 1));
    }

    /// <summary>
    /// A source contributing nothing is normal — most conventional directories do not exist on any
    /// given machine — and must not produce a phantom artifact.
    /// </summary>
    [TestMethod]
    public void AnEmptySourceContributesNothing()
    {
        Assert.AreEqual(
            0,
            ArtifactResolver.Resolve([new FakeSource("empty", ArtifactKind.Plan, Global)]).Count);
    }
}

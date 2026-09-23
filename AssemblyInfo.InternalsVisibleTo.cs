// AssemblyInfo.InternalsVisibleTo.cs — the solution's friend grants, in one place.
//
// ══ WHY THIS FILE EXISTS ══
//
// Every assembly in this solution links this ONE file, so the whole friend-grant set is read and
// edited in a single place instead of being reconstructed from a dozen csproj files. It is
// LINKED, never copied: see the <Compile Include="../../AssemblyInfo.InternalsVisibleTo.cs" />
// item in each project, which uses a RELATIVE path with forward slashes so the Linux and macOS
// builds this repo publishes for resolve it the same way Windows does.
//
// ⚠ IT IS COMPILED INTO EVERY LINKING ASSEMBLY, so every assembly grants internals to every
// project named below. That is coarser than the per-project grants this replaced, and the
// coarseness is the deliberate trade: one list that is obviously complete, against a dozen
// lists that were individually precise and collectively unknowable.
//
// ⛔ ADDING A NAME HERE GRANTS IT ACCESS TO EVERY ASSEMBLY, not just the one you had in mind.
// Think of it as solution-internal rather than assembly-internal. If a member genuinely should
// not be reachable from another assembly, `internal` is no longer the way to say so here — make
// the member private, or keep the type out of this solution.
//
// ⚠ A NAME HERE IS AN ASSEMBLY NAME, NOT A PROJECT PATH OR A ROOT NAMESPACE. Every project in
// this repo builds an assembly named exactly after its directory — `AgentForge.Core` produces
// `AgentForge.Core.dll` — while root namespaces are `Bennewitz.Ninja.*`. A grant naming the
// namespace form compiles, ships, and silently grants NOTHING, because no such assembly exists.
// Some prose in this repo still spells one that way; the names below are the assembly names.
//
// ⚠ NOT STRONG-NAMED, and eleven of these assemblies ship as NuGet packages. An unsigned
// InternalsVisibleTo is satisfied by any assembly that simply calls itself by the right name, so
// these grants travel with the packages. That was already true of the per-project grants this
// replaces — `AgentForge.Sdk` alone named eight — so it is not a new exposure, but it is worth
// knowing before adding a name.
//
// ⓘ Layering is NOT affected. AssemblyLayeringTests enforces the product/neutral boundary over
// project REFERENCES; an attribute is not a dependency, and CLAUDE.md says so explicitly. What
// this file costs is narrowness, not the layering contract.

using System.Runtime.CompilerServices;

// ── Shipping libraries: the neutral stack ────────────────────────────────────────────────────
[assembly: InternalsVisibleTo("AgentForge.Abstractions")]
[assembly: InternalsVisibleTo("AgentForge.Artifacts")]
[assembly: InternalsVisibleTo("AgentForge.Avalonia.Shell")]
[assembly: InternalsVisibleTo("AgentForge.Core")]
[assembly: InternalsVisibleTo("AgentForge.Sdk")]

// ── Shipping library: a family of one, deliberately outside the AgentForge.* prefix ──────────
[assembly: InternalsVisibleTo("JsonC")]

// ── Product-specific halves and the two app assemblies ───────────────────────────────────────
[assembly: InternalsVisibleTo("ClaudeForge")]
[assembly: InternalsVisibleTo("ClaudeForge.Avalonia")]
[assembly: InternalsVisibleTo("ClaudeForge.Sdk.Claude")]

// ── Test projects ────────────────────────────────────────────────────────────────────────────
// ⓘ These are why most of the per-project grants existed at all: a test reaching an internal
// seam. Listing every one of them here is what makes a new test project work without a csproj
// edit — and what makes the cost above concrete, since each name is also a grant against every
// shipping assembly.
[assembly: InternalsVisibleTo("AgentForge.Artifacts.Tests")]
[assembly: InternalsVisibleTo("AgentForge.Core.Tests")]
[assembly: InternalsVisibleTo("AgentForge.Sdk.Tests")]
[assembly: InternalsVisibleTo("ClaudeForge.Avalonia.Tests")]
[assembly: InternalsVisibleTo("ClaudeForge.Sdk.Claude.Tests")]
[assembly: InternalsVisibleTo("ClaudeForge.Tests")]
[assembly: InternalsVisibleTo("JsonC.Tests")]

// ══ WHAT THE GRANTS ARE ACTUALLY FOR ═════════════════════════════════════════════════════════
//
// ⚠ Carried over from the per-project grants this file replaced. Each grant used to sit beside a
// comment explaining the one seam it existed for; collapsing to a single list would have thrown
// that away, and two of these entries are the only record that a seam is meant to RETIRE.
//
// The list is no longer 1:1 with a grant — every name above reaches everything — so read this as
// "which internals are load-bearing across assemblies, and why they are internal".
//
// ── Internals that traffic in JsonNode, kept off the public SDK surface on purpose ──
//   AgentConfigClientCore.GetEffectiveNode / GetScopeValue / RaiseChangedFromAccessor
//       Used by the Claude-domain half's accessors. Internal because they expose JsonNode, which
//       the public SDK surface deliberately keeps out — see HookEvent.PreservedFields for the
//       same call. Promoting them to public to serve one consumer was judged the worse trade.
//   SnapshotDirtyDocuments() / DirtyDocumentSnapshot
//       Read by the neutral shell's save-confirmation dialog builder, for that dialog only. Same
//       reasoning, same decision.
//   HookEvent.PreservedFields
//       Inspected by the Claude SDK's tests when asserting round-trips.
//
// ── Internals that exist because a public replacement has not been designed ──
//   ⏳ AgentConfigClientCore.WorkspaceForGui — RETIRE WHEN A PUBLIC SURFACE EXISTS.
//       Both GUIs reach it to hand the loaded workspace to the shell's settings page. It is
//       internal because it exposes a MUTABLE SettingsWorkspace; the public surface that would
//       replace it has not been designed. Both apps' grants retire on the same day.
//   ⏳ AgentConfigClientCore.FromExistingWorkspace — RETIRE WITH THE 4.3.7 WRAP PATH.
//       The GUI wraps a pre-loaded SettingsWorkspace to share state with the SDK during the
//       editor-VM migration, and ClaudeForge.Tests uses the same overload to wrap an in-memory
//       workspace without real disk I/O. Once 4.3.7 removes the legacy workspace fields from
//       MainWindowViewModel, both uses go and the GUI consumes only the public SDK surface.
//
// ── Internals a second implementation would otherwise force public ──
//   BackupClient — the only IBackupClient implementation, returned from CreateBackupClient.
//       Making it public to serve the second product would widen the SDK permanently.
//   BackupJsonContext — the tests exercise the exact serialisation path production uses under
//       trimming, which means reaching the internal context rather than a stand-in.
//
// ── Internals whose public form is deliberately harder to test ──
//   SearchViewModel.ExecuteSearch — internal because the public surface is the DEBOUNCED
//       SearchQuery property, and a unit test cannot drive a 200 ms debounce that pivots through
//       the UI dispatcher. Both apps' tests assert this same seam.
//
// ⓘ EssentialsCardViewModel.SetFilteredOptions / IsLoading are NOT in this list, and that is the
// interesting case: they were made PUBLIC rather than granted, on the argument that an app is an
// ordinary consumer of the shell and a friend grant "would have quietly exposed every other
// internal too". That argument has been overtaken by this file — the blanket grant now exists —
// but the members stay public, because public is the honest shape for something a separate
// assembly is expected to call.

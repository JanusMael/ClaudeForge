using Bennewitz.Ninja.AgentForge.Avalonia.Shell.Adapters;
using Bennewitz.Ninja.AgentForge.Core.Settings;
using Bennewitz.Ninja.LayeredEditors.Abstractions;
using Bennewitz.Ninja.OpenCode.Sdk;
using Bennewitz.Ninja.OpenCodeForge.Adapters;

namespace Bennewitz.Ninja.OpenCodeForge.Tests;

/// <summary>
/// Scope escalation, asserted against the scope object the RUNNING APP hands the classifier —
/// not a test double built from the same constant the policy compares against.
/// </summary>
/// <remarks>
/// <para>
/// ⛔⛔ <b>The existing escalation tests cannot see this class of bug.</b> They construct
/// <c>new Scope(OpenCodeScopes.Project)</c>, so the id under test is the very constant
/// <c>IsGitCommittedScope</c> compares to — the assertion is tautological with respect to casing
/// and spelling. In the app an editor's <c>EditingScope</c> is a
/// <see cref="ConfigScopeAdapter"/>, whose <c>Id</c> is
/// <see cref="ConfigScope.Id"/> — and that is lower-cased at source.
/// </para>
/// <para>
/// This test therefore pins the escalation to the real adapter. If it fails, the feature is
/// inert in the product while every table-level test stays green.
/// </para>
/// </remarks>
[TestClass]
public sealed class ScopeEscalationRealScopeTests
{
    private const string ConcreteApiKeyPath = "provider.anthropic.options.apiKey";

    [TestMethod]
    public void TheAppsOwnProjectScopeEscalatesAnApiKey()
    {
        IEditorScope project = ConfigScopeAdapter.For(ConfigScope.Project);
        IEditorScope user = ConfigScopeAdapter.For(ConfigScope.User);

        DangerAssessment atUser =
            OpenCodeDangerTable.Config.Classify(ConcreteApiKeyPath, user, "sk-live-xxx");
        DangerAssessment atProject =
            OpenCodeDangerTable.Config.Classify(ConcreteApiKeyPath, project, "sk-live-xxx");

        Assert.AreEqual(AppSeverity.Caution, atUser.Severity,
            "Premise: a user-global secret stays Caution.");
        Assert.AreEqual(AppSeverity.Critical, atProject.Severity,
            $"A plaintext API key in a git-committed project file must escalate to Critical. The "
            + $"app's project scope reports Id='{project.Id}', and the policy's predicate compares "
            + $"ordinally against OpenCodeScopes.Project='{OpenCodeScopes.Project}'. If those two "
            + "strings differ, escalation is inert everywhere in the product while the table-level "
            + "tests — which build their own scope from that same constant — stay green.");
    }

    /// <summary>
    /// Pins the exact relationship between a ladder rung's NAME and a scope's ID, so a failure
    /// here names the cause rather than the symptom — and so the comparison cannot be "tidied"
    /// back to <see cref="StringComparison.Ordinal"/>.
    /// </summary>
    [TestMethod]
    public void AScopeIdIsItsRungNameLowerCasedWhichIsWhyTheComparisonIgnoresCase()
    {
        string id = ConfigScopeAdapter.For(ConfigScope.Project).Id;

        Assert.IsTrue(
            string.Equals(OpenCodeScopes.Project, id, StringComparison.OrdinalIgnoreCase),
            $"A scope id must still BE the rung name, only cased differently — got '{id}'. If "
            + "these have genuinely diverged, the predicate needs a real mapping rather than a "
            + "case-insensitive compare.");

        Assert.IsFalse(
            string.Equals(OpenCodeScopes.Project, id, StringComparison.Ordinal),
            "⛔ These deliberately differ in case: OpenCodeScopes.Project is the ladder RUNG NAME "
            + "and ConfigScope.Id lower-cases it. That is why IsGitCommittedScope must compare "
            + "with OrdinalIgnoreCase. Changing it back to Ordinal makes the apiKey escalation "
            + "inert in the product while leaving every table-level test green — which is exactly "
            + "how the bug shipped in the first place.");
    }
}

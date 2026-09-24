using Bennewitz.Ninja.AgentForge.Abstractions.Configuration;
using Bennewitz.Ninja.AgentForge.Core.Platform;
using Bennewitz.Ninja.AgentForge.Core.Schema;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.Schema;

/// <summary>
/// Guards that each product's <see cref="ProductDescriptor"/> resolves to <b>its own</b>
/// schema.
/// <para>
/// Written because nothing did. Phase 4 replaced <c>bool IsClaudeCode</c> with a
/// descriptor, and the refactor was canaried by pointing Claude Desktop's descriptor at
/// Claude Code's schema: <b>all 2,798 tests still passed</b>. Desktop's schema selection
/// and its "no hooks" behaviour were entirely unguarded, so a transposition of the two
/// products would have validated Desktop configs against the wrong schema — and offered
/// the Hooks editor for a product that has none — with a green suite.
/// </para>
/// </summary>
public class ProductDescriptorSchemaTests
{
    private const string ClaudeCodeSchemaFile = "claude-code-settings.json";
    private const string ClaudeDesktopSchemaFile = "claude-desktop-config.json";

    [Fact]
    public void Descriptors_NameDistinctProductsAndSchemas()
    {
        ProductDescriptor code = SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty);
        ProductDescriptor desktop = SchemaRegistry.ClaudeDesktopProduct;

        Assert.Equal("claude-code", code.Id);
        Assert.Equal(ClaudeCodeSchemaFile, code.SchemaFileName);

        Assert.Equal("claude-desktop", desktop.Id);
        Assert.Equal(ClaudeDesktopSchemaFile, desktop.SchemaFileName);

        MessageAssert.NotEqual(code.SchemaFileName, desktop.SchemaFileName,
            "The two products must not resolve to the same schema file.");
    }

    /// <summary>
    /// The behavioural half: the descriptors must load schemas that are actually
    /// different. Asserted on properties unique to each product rather than on the file
    /// name, so the test fails if the file names are right but the loading chain returns
    /// the wrong document.
    /// </summary>
    [Fact]
    public async Task Descriptors_LoadTheSchemaBelongingToTheirOwnProduct()
    {
        SchemaRegistry registry = new();

        // var: GetSettingsNodeAsync returns Json.Schema.JsonSchemaNode, and importing that
        // namespace here collides with Core's own Schema types (same reason
        // NavigationTreeBuilderThreadSafetyTests uses var).
        var codeRoot =
            await registry.GetSettingsNodeAsync(SchemaRegistry.ClaudeCodeProductFor(ClaudeEnvironment.Empty), TestContext.Current.CancellationToken);
        var desktopRoot =
            await registry.GetSettingsNodeAsync(SchemaRegistry.ClaudeDesktopProduct, TestContext.Current.CancellationToken);

        IReadOnlyList<SchemaNode> codeNodes = SchemaTreeBuilder.BuildTopLevel(codeRoot);
        IReadOnlyList<SchemaNode> desktopNodes = SchemaTreeBuilder.BuildTopLevel(desktopRoot);

        Assert.True(codeNodes.Any(n => n.Name == "permissions"),
            "Claude Code's settings schema declares 'permissions'.");
        Assert.False(desktopNodes.Any(n => n.Name == "permissions"),
            "Claude Desktop's config schema does not — if it appears here, Desktop loaded "
            + "Claude Code's schema.");
    }

    /// <summary>
    /// Pins the behaviour that let the <c>IsClaudeCode</c> ternary be deleted outright
    /// rather than replaced. The old code returned an empty list for Desktop because it
    /// was <i>told</i> to; the new code asks Desktop's schema and finds no hooks in it.
    /// Those agree only for as long as Desktop's schema genuinely declares none.
    /// </summary>
    [Fact]
    public void HookMetadata_IsDeclaredByClaudeCodesSchemaAndAbsentFromDesktops()
    {
        Assert.True(SchemaRegistry.GetHookEvents(ClaudeCodeSchemaFile).Count > 0,
            "Claude Code's schema declares hook events.");
        Assert.True(SchemaRegistry.GetHookCommandVariants(ClaudeCodeSchemaFile).Count > 0,
            "Claude Code's schema declares hook command variants.");

        MessageAssert.Equal(0, SchemaRegistry.GetHookEvents(ClaudeDesktopSchemaFile).Count,
            "Desktop has no hooks. The client no longer hardcodes that — it reads it here.");
        MessageAssert.Equal(0, SchemaRegistry.GetHookCommandVariants(ClaudeDesktopSchemaFile).Count,
            "Desktop has no hook command variants, for the same reason.");
    }

}

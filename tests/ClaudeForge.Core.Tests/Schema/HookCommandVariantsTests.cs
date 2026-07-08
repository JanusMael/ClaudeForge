using Bennewitz.Ninja.ClaudeForge.Core.Schema;

namespace Bennewitz.Ninja.ClaudeForge.Core.Tests.Schema;

/// <summary>
/// Locks <see cref="SchemaRegistry.GetHookCommandVariants"/>: it reads the hook command
/// variants (type + description + field descriptions) straight from the bundled schema's
/// <c>$defs.hookCommand.anyOf</c> — the raw-JSON path used because the <c>anyOf</c> variants
/// don't survive the flattened <see cref="SchemaNode"/> tree. This is the SDK-first source
/// that replaces the editor's hardcoded command-type mirror.
/// </summary>
[TestClass]
public sealed class HookCommandVariantsTests
{
    private const string SchemaFile = "claude-code-settings.json";

    [TestMethod]
    public void GetHookCommandVariants_ReadsBundledSchema_TypesAndDescriptions()
    {
        IReadOnlyList<HookCommandVariantInfo> variants = SchemaRegistry.GetHookCommandVariants(SchemaFile);

        // The bundled schema defines the standard hook command shapes; the parse must
        // surface each variant keyed by its `type` const, carrying the schema description.
        HookCommandVariantInfo command = variants.First(v => v.Type == "command");
        StringAssert.Contains(command.Description!, "Bash command hook");

        HookCommandVariantInfo prompt = variants.First(v => v.Type == "prompt");
        StringAssert.Contains(prompt.Description!, "LLM prompt hook");

        HookCommandVariantInfo http = variants.First(v => v.Type == "http");
        StringAssert.Contains(http.Description!, "HTTP webhook hook");
    }

    [TestMethod]
    public void GetHookCommandVariants_CarriesFieldDescriptions_ExcludingDiscriminator()
    {
        HookCommandVariantInfo command = SchemaRegistry
            .GetHookCommandVariants(SchemaFile)
            .First(v => v.Type == "command");

        // Field descriptions flow through for the per-field tooltips headless consumers use.
        HookFieldInfo ifField = command.Fields.First(f => f.Name == "if");
        StringAssert.Contains(ifField.Description!, "permission-rule-syntax");

        Assert.IsTrue(command.Fields.Any(f => f.Name == "timeout"),
            "The command variant's fields must include timeout.");

        // The `type` discriminator is captured as Type, not surfaced as a field — its
        // bare "Hook type" description adds nothing as a tooltip.
        Assert.IsFalse(command.Fields.Any(f => f.Name == "type"),
            "The type discriminator must not appear in the field list.");
    }

    [TestMethod]
    public void GetHookCommandVariants_MissingResource_ReturnsEmpty()
    {
        // A schema filename with no bundled resource → fail-open empty list, never a throw.
        Assert.AreEqual(0, SchemaRegistry.GetHookCommandVariants("does-not-exist.json").Count);
    }
}

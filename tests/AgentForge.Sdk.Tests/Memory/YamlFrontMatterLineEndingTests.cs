using Bennewitz.Ninja.AgentForge.Sdk.Memory;

namespace Bennewitz.Ninja.AgentForge.Sdk.Tests.Memory;

/// <summary>
/// Line-ending fidelity for <see cref="YamlFrontMatter"/>.
///
/// <para>
/// <see cref="YamlFrontMatter.Parse"/> strips the <c>\r</c> from each line and
/// joins multi-line <c>RawText</c> with <c>\n</c>. <see cref="YamlFrontMatter.Compose"/>
/// then picks its line ending from the body and emits that <c>RawText</c> verbatim —
/// so a CRLF file whose block scalar came back untouched used to end up with LF
/// inside the block and CRLF everywhere else. Opening a Windows skill file and
/// saving it without edits rewrote its line endings.
/// </para>
///
/// <para>
/// These fixtures spell their newlines out rather than relying on a raw string
/// literal, whose endings depend on how git checked the test file out — which is
/// exactly why this only failed on Windows CI and passed locally.
/// </para>
/// </summary>
/// <remarks>
/// ⓘ <b>Ported from <c>main</c> on 2026-09-16</b>, where this coverage was written independently
/// of the branch's own block-scalar suite. Both are kept: 10 of these failed against the branch's
/// parser and 3 of the branch's failed against main's, so neither side was a superset and the
/// implementation here is the union. See <c>YamlFrontMatterBlockScalarTests</c> for the sibling.
/// </remarks>
public sealed class YamlFrontMatterLineEndingTests
{
    private static string Join(string nl, params string[] lines)
    {
        return string.Join(nl, lines) + nl;
    }

    private static string[] BlockScalarLines =>
    [
        "---",
        "name: address-pr-feedback",
        "description: >-",
        "  Fetch, vet, and act on review feedback left on a pull request.",
        "  Use this WHENEVER the developer says there is review feedback.",
        "---",
        "",
        "# Address PR feedback",
    ];

    private static string[] NestedMappingLines =>
    [
        "---",
        "name: note",
        "metadata:",
        "  node_type: memory",
        "  type: project",
        "---",
        "",
        "Body.",
    ];

    private static string[] BlockListLines =>
    [
        "---",
        "name: agent",
        "tools:",
        "  - Read",
        "  - Grep",
        "---",
        "",
        "Body.",
    ];

    [Fact]
    public void Crlf_BlockScalar_RoundTripsByteForByte()
    {
        string text = Join("\r\n", BlockScalarLines);

        Assert.Equal(text, YamlFrontMatter.Compose(YamlFrontMatter.Parse(text)));
    }

    [Fact]
    public void Crlf_NestedMapping_RoundTripsByteForByte()
    {
        string text = Join("\r\n", NestedMappingLines);

        Assert.Equal(text, YamlFrontMatter.Compose(YamlFrontMatter.Parse(text)));
    }

    [Fact]
    public void Crlf_BlockList_RoundTripsByteForByte()
    {
        string text = Join("\r\n", BlockListLines);

        Assert.Equal(text, YamlFrontMatter.Compose(YamlFrontMatter.Parse(text)));
    }

    [Fact]
    public void Crlf_ComposedFile_HasNoBareLineFeed()
    {
        string composed = YamlFrontMatter.Compose(
            YamlFrontMatter.Parse(Join("\r\n", BlockScalarLines)));

        for (int i = 0; i < composed.Length; i++)
        {
            if (composed[i] == '\n')
            {
                Assert.True(
                    i > 0 && composed[i - 1] == '\r',
                    "A CRLF file must not come back with a bare LF at index " + i
                    + " — that is a mixed-ending file written into the user's repository.");
            }
        }
    }

    [Fact]
    public void Lf_BlockScalar_RoundTripsByteForByte()
    {
        string text = Join("\n", BlockScalarLines);

        Assert.Equal(text, YamlFrontMatter.Compose(YamlFrontMatter.Parse(text)));
    }

    [Fact]
    public void Lf_ComposedFile_HasNoCarriageReturn()
    {
        string composed = YamlFrontMatter.Compose(
            YamlFrontMatter.Parse(Join("\n", BlockScalarLines)));

        Assert.False(
            composed.Contains('\r'),
            "An LF file must not pick up carriage returns on the way back out.");
    }

    /// <summary>
    /// The edited path re-renders rather than replaying RawText, so it has its own
    /// way to get this wrong.
    /// </summary>
    [Fact]
    public void Crlf_EditedBlockScalar_StaysCrlf()
    {
        string text = Join("\r\n", BlockScalarLines);

        string composed = YamlFrontMatter.Compose(
            YamlFrontMatter.Parse(text).WithScalar("description", "Something shorter now."));

        OrdinalAssert.Contains("description: >-\r\n", composed);
        Assert.False(
            composed.Contains("\n\n", StringComparison.Ordinal),
            "A bare LF pair means a re-rendered line was emitted with the wrong ending.");
    }
}

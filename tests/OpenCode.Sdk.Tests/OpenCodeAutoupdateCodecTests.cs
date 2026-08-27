using System.Text.Json;
using Bennewitz.Ninja.OpenCode.Sdk.Updates;

namespace Bennewitz.Ninja.OpenCode.Sdk.Tests;

/// <summary>
/// The <c>autoupdate</c> value: <c>true</c> | <c>false</c> | <c>"notify"</c>, plus absent.
/// </summary>
/// <remarks>
/// Small, and the interesting part is what it REFUSES to do: it does not fold absent into
/// <c>false</c>, and it does not quietly correct <c>"Notify"</c> into <c>"notify"</c>.
/// </remarks>
[TestClass]
public sealed class OpenCodeAutoupdateCodecTests
{
    [TestMethod]
    public void AnAbsentKey_IsNotSet_AndWritesNothing()
    {
        OpenCodeAutoupdateConfig config = OpenCodeAutoupdateCodec.Read(null, isDefined: false);

        Assert.AreEqual(OpenCodeAutoupdateMode.NotSet, config.Mode);
        Assert.IsNull(OpenCodeAutoupdateCodec.Write(config));
    }

    [TestMethod]
    public void TheThreeValues_EachRoundTrip()
    {
        (object Value, OpenCodeAutoupdateMode Mode)[] cases =
        [
            (true, OpenCodeAutoupdateMode.Automatic),
            (false, OpenCodeAutoupdateMode.Disabled),
            ("notify", OpenCodeAutoupdateMode.Notify),
        ];

        foreach ((object value, OpenCodeAutoupdateMode mode) in cases)
        {
            OpenCodeAutoupdateConfig config = OpenCodeAutoupdateCodec.Read(value, isDefined: true);

            Assert.AreEqual(mode, config.Mode, $"Misread {JsonSerializer.Serialize(value)}.");
            Assert.AreEqual(
                value,
                OpenCodeAutoupdateCodec.Write(config),
                $"{JsonSerializer.Serialize(value)} did not survive a round trip.");
        }
    }

    /// <remarks>
    /// ⚠ Absent and <c>false</c> both mean "do not update", so folding them looks harmless. It is
    /// not: one is a key the user never wrote and the other is a decision they recorded, and an
    /// editor that rewrites either into the other turns opening a settings page into a diff.
    /// </remarks>
    [TestMethod]
    public void AbsentAndFalse_AreDifferentStates()
    {
        OpenCodeAutoupdateConfig absent = OpenCodeAutoupdateCodec.Read(null, isDefined: false);
        OpenCodeAutoupdateConfig off = OpenCodeAutoupdateCodec.Read(false, isDefined: true);

        Assert.AreNotEqual(absent.Mode, off.Mode);
        Assert.IsNull(OpenCodeAutoupdateCodec.Write(absent));
        Assert.AreEqual(false, OpenCodeAutoupdateCodec.Write(off));
    }

    /// <remarks>
    /// ⚠⚠ <b>The behaviour most likely to be "improved" into a bug.</b> The schema's enum is exactly
    /// <c>["notify"]</c>, so <c>"Notify"</c> is invalid. Reading it as <see
    /// cref="OpenCodeAutoupdateMode.Notify"/> would mean the next save silently replaced the user's
    /// text with a different string — a fix they never asked for, applied to a file they may not
    /// have written. Held verbatim, they get to see it.
    /// </remarks>
    [TestMethod]
    public void TheNotifyLiteral_IsCaseSensitive_AndAVariantIsHeldVerbatim()
    {
        foreach (string variant in new[] { "Notify", "NOTIFY", " notify", "notify " })
        {
            OpenCodeAutoupdateConfig config = OpenCodeAutoupdateCodec.Read(variant, isDefined: true);

            Assert.AreEqual(
                OpenCodeAutoupdateMode.Unrecognised,
                config.Mode,
                $"'{variant}' was accepted as the notify literal. The schema's enum is exactly "
                + "[\"notify\"], so this would read a value OpenCode rejects as if it were valid — "
                + "and then rewrite it on save.");
            Assert.AreEqual(
                variant,
                OpenCodeAutoupdateCodec.Write(config),
                $"'{variant}' was not preserved verbatim.");
        }
    }

    [TestMethod]
    public void AnUnrelatedValue_IsHeldVerbatim()
    {
        OpenCodeAutoupdateConfig config = OpenCodeAutoupdateCodec.Read(5L, isDefined: true);

        Assert.AreEqual(OpenCodeAutoupdateMode.Unrecognised, config.Mode);
        Assert.AreEqual(5L, OpenCodeAutoupdateCodec.Write(config));
    }

    /// <remarks>
    /// The same currency limitation the tooling mode carries, pinned rather than hoped about.
    /// </remarks>
    [TestMethod]
    public void AnExplicitNull_ReadsAsUnrecognised_AndCannotBeWrittenBack()
    {
        OpenCodeAutoupdateConfig config = OpenCodeAutoupdateCodec.Read(null, isDefined: true);

        Assert.AreEqual(OpenCodeAutoupdateMode.Unrecognised, config.Mode);
        Assert.IsNull(OpenCodeAutoupdateCodec.Write(config));
    }

    // ── Schema drift ───────────────────────────────────────────────────────────

    private static JsonDocument Schema()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            string candidate = Path.Combine(
                dir, "src", "AgentForge.Core", "Assets", "Schemas", "opencode-config.json");
            if (File.Exists(candidate))
            {
                return JsonDocument.Parse(File.ReadAllText(candidate));
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            $"Could not locate the bundled OpenCode schema from '{AppContext.BaseDirectory}'.");
    }

    /// <remarks>
    /// If a refresh adds a fourth value — say <c>"prompt"</c> — the picker would silently hold it
    /// verbatim rather than offering it, and the user would have no way to select the new behaviour.
    /// This is the assertion that turns that into a build failure.
    /// </remarks>
    [TestMethod]
    public void TheSchemaStillDeclaresBooleanPlusExactlyOneStringLiteral()
    {
        using JsonDocument doc = Schema();

        JsonElement arms = doc.RootElement
            .GetProperty("$defs").GetProperty("Config").GetProperty("properties")
            .GetProperty("autoupdate").GetProperty("anyOf");

        Assert.AreEqual(
            2, arms.GetArrayLength(), "`autoupdate` no longer has exactly two arms.");
        Assert.AreEqual("boolean", arms[0].GetProperty("type").GetString());
        Assert.AreEqual("string", arms[1].GetProperty("type").GetString());

        JsonElement values = arms[1].GetProperty("enum");
        Assert.AreEqual(
            1,
            values.GetArrayLength(),
            "The string arm's enum gained or lost a value. The picker offers exactly three choices, "
            + "so a fourth would be unreachable and a removed one would be offered but rejected.");
        Assert.AreEqual(
            OpenCodeAutoupdateCodec.NotifyLiteral,
            values[0].GetString(),
            "The string arm's single literal changed. The codec writes the constant, so the editor "
            + "would now be producing a value the schema does not admit.");
    }
}

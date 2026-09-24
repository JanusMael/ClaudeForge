using Bennewitz.Ninja.ClaudeForge.Services;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Services;

/// <summary>
/// Locks each user-actionable schema-error translation produced by
/// <see cref="SchemaErrorMessages.Friendly"/>. Real validator-emitted
/// <c>(InstancePath, Message)</c> shapes are exercised end-to-end in
/// <c>HookUnknownEventValidationTests</c> and similar integration suites;
/// these tests pin the post-translation strings so a future refactor of
/// the schema (which would change the InstancePath) immediately surfaces
/// here instead of silently regressing the user-facing error text.
/// </summary>
public sealed class SchemaErrorMessagesTests
{
    private static SchemaValidationError Make(string path, string message)
    {
        return new SchemaValidationError("settings.json", path, message);
    }

    // ── Permission rule errors (pre-existing branch — sanity coverage) ──

    [Fact]
    public void Friendly_PermissionRuleError_ProducesActionableHelp()
    {
        SchemaValidationError err = Make("/permissions/allow/0", "Some raw schema gibberish");
        string msg = SchemaErrorMessages.Friendly(err);

        OrdinalAssert.Contains("Invalid permission rule syntax", msg);
        OrdinalAssert.Contains("Bash(*)", msg);
    }

    // ── Unknown hook event ───────────────────────────────────────────────

    [Fact]
    public void Friendly_UnknownHookEvent_PreToolPattern_SuggestsMatcher()
    {
        // The user's exact 2026-05-01 mistake: picked "PreBashToolUse" from
        // the editor's left rail (the bogus entry has since been removed).
        SchemaValidationError err = Make("/hooks/PreBashToolUse", "All values fail against the false schema");
        string msg = SchemaErrorMessages.Friendly(err);

        MessageAssert.Contains("PreBashToolUse", msg,
            "Message should name the offending event so the user can locate it.");
        MessageAssert.Contains("PreToolUse", msg,
            "Message should suggest the canonical event name.");
        MessageAssert.Contains("Bash", msg,
            "Message should suggest the tool name as the matcher.");
        Assert.False(msg.Contains("false schema"),
            "Translated message must not leak JsonSchema.Net validator jargon.");
    }

    [Fact]
    public void Friendly_UnknownHookEvent_PostToolPattern_SuggestsMatcher()
    {
        SchemaValidationError err = Make("/hooks/PostFileEditToolUse", "All values fail against the false schema");
        string msg = SchemaErrorMessages.Friendly(err);

        OrdinalAssert.Contains("PostToolUse", msg);
        OrdinalAssert.Contains("FileEdit", msg);
    }

    [Fact]
    public void Friendly_UnknownHookEvent_NonToolPattern_GenericGuidance()
    {
        // Made-up event that doesn't match the Pre/Post<Tool>ToolUse regex.
        SchemaValidationError err = Make("/hooks/Wibble", "All values fail against the false schema");
        string msg = SchemaErrorMessages.Friendly(err);

        OrdinalAssert.Contains("Wibble", msg);
        OrdinalAssert.Contains("not a recognised hook event", msg);
        MessageAssert.Contains("PreToolUse", msg,
            "Generic-pattern message should still hint at the standard event names.");
    }

    [Fact]
    public void Friendly_UnrecognisedError_FallsThroughToRawMessage()
    {
        // Anything not matched by the translation table must surface the
        // raw validator message verbatim — better an opaque message than
        // a misleading translation.
        SchemaValidationError err = Make("/some/unrelated/path", "minLength constraint failed");
        string msg = SchemaErrorMessages.Friendly(err);

        Assert.Equal("minLength constraint failed", msg);
    }

    // ── Format envelope ─────────────────────────────────────────────────

    [Fact]
    public void Format_SingleError_RendersBulletedBlock()
    {
        SchemaValidationError[] errors =
        [
            Make("/hooks/PreBashToolUse", "All values fail against the false schema"),
        ];

        string rendered = SchemaErrorMessages.Format(errors);

        OrdinalAssert.Contains("1 validation error was found", rendered);
        OrdinalAssert.Contains("settings.json:", rendered);
        OrdinalAssert.Contains("•", rendered);
        MessageAssert.Contains("PreToolUse", rendered,
            "Format should embed the friendly message, not the raw validator text.");
        Assert.False(rendered.Contains("false schema"),
            "The bulleted block must use the friendly translation, not the raw validator jargon.");
    }

    [Fact]
    public void Format_EnumError_ShowsCurrentValueAndAllowedValues()
    {
        // The killer case: "should match one of the enum values" alone doesn't tell the
        // user what they HAVE or what's ALLOWED. The enriched error carries both.
        SchemaValidationError[] errors =
        [
            new SchemaValidationError("settings.local.json", "/effortLevel",
                "Value should match one of the values specified by the enum")
            {
                Value = "\"max\"",
                AllowedValues = ["low", "medium", "high", "xhigh"],
            },
        ];

        string rendered = SchemaErrorMessages.Format(errors);

        MessageAssert.Contains("current value: \"max\"", rendered,
            "The offending value should be shown so the user sees what they have.");
        MessageAssert.Contains("allowed values: low, medium, high, xhigh", rendered,
            "The permitted enum values should be listed so the user knows the valid options.");
        MessageAssert.Contains("(Local scope)", rendered,
            "settings.local.json should be labelled with its scope.");
    }

    [Fact]
    public void Format_UnenrichedError_RendersExactlyAsBefore()
    {
        // Errors without Value/AllowedValues (the common path) must not gain blank
        // detail lines — the enrichment is strictly additive.
        SchemaValidationError[] errors = [Make("/some/path", "minLength constraint failed")];

        string rendered = SchemaErrorMessages.Format(errors);

        Assert.False(rendered.Contains("current value:"),
            "No value line should appear when the error carries no Value.");
        Assert.False(rendered.Contains("allowed values:"),
            "No allowed-values line should appear when the error carries no AllowedValues.");
    }
}
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
[TestClass]
public sealed class SchemaErrorMessagesTests
{
    private static SchemaValidationError Make(string path, string message)
    {
        return new SchemaValidationError("settings.json", path, message);
    }

    // ── Permission rule errors (pre-existing branch — sanity coverage) ──

    [TestMethod]
    public void Friendly_PermissionRuleError_ProducesActionableHelp()
    {
        SchemaValidationError err = Make("/permissions/allow/0", "Some raw schema gibberish");
        string msg = SchemaErrorMessages.Friendly(err);

        StringAssert.Contains(msg, "Invalid permission rule syntax");
        StringAssert.Contains(msg, "Bash(*)");
    }

    // ── Unknown hook event ───────────────────────────────────────────────

    [TestMethod]
    public void Friendly_UnknownHookEvent_PreToolPattern_SuggestsMatcher()
    {
        // The user's exact 2026-05-01 mistake: picked "PreBashToolUse" from
        // the editor's left rail (the bogus entry has since been removed).
        SchemaValidationError err = Make("/hooks/PreBashToolUse", "All values fail against the false schema");
        string msg = SchemaErrorMessages.Friendly(err);

        StringAssert.Contains(msg, "PreBashToolUse",
            "Message should name the offending event so the user can locate it.");
        StringAssert.Contains(msg, "PreToolUse",
            "Message should suggest the canonical event name.");
        StringAssert.Contains(msg, "Bash",
            "Message should suggest the tool name as the matcher.");
        Assert.IsFalse(msg.Contains("false schema"),
            "Translated message must not leak JsonSchema.Net validator jargon.");
    }

    [TestMethod]
    public void Friendly_UnknownHookEvent_PostToolPattern_SuggestsMatcher()
    {
        SchemaValidationError err = Make("/hooks/PostFileEditToolUse", "All values fail against the false schema");
        string msg = SchemaErrorMessages.Friendly(err);

        StringAssert.Contains(msg, "PostToolUse");
        StringAssert.Contains(msg, "FileEdit");
    }

    [TestMethod]
    public void Friendly_UnknownHookEvent_NonToolPattern_GenericGuidance()
    {
        // Made-up event that doesn't match the Pre/Post<Tool>ToolUse regex.
        SchemaValidationError err = Make("/hooks/Wibble", "All values fail against the false schema");
        string msg = SchemaErrorMessages.Friendly(err);

        StringAssert.Contains(msg, "Wibble");
        StringAssert.Contains(msg, "not a recognised hook event");
        StringAssert.Contains(msg, "PreToolUse",
            "Generic-pattern message should still hint at the standard event names.");
    }

    [TestMethod]
    public void Friendly_UnrecognisedError_FallsThroughToRawMessage()
    {
        // Anything not matched by the translation table must surface the
        // raw validator message verbatim — better an opaque message than
        // a misleading translation.
        SchemaValidationError err = Make("/some/unrelated/path", "minLength constraint failed");
        string msg = SchemaErrorMessages.Friendly(err);

        Assert.AreEqual("minLength constraint failed", msg);
    }

    // ── Format envelope ─────────────────────────────────────────────────

    [TestMethod]
    public void Format_SingleError_RendersBulletedBlock()
    {
        SchemaValidationError[] errors =
        [
            Make("/hooks/PreBashToolUse", "All values fail against the false schema"),
        ];

        string rendered = SchemaErrorMessages.Format(errors);

        StringAssert.Contains(rendered, "1 validation error was found");
        StringAssert.Contains(rendered, "settings.json:");
        StringAssert.Contains(rendered, "•");
        StringAssert.Contains(rendered, "PreToolUse",
            "Format should embed the friendly message, not the raw validator text.");
        Assert.IsFalse(rendered.Contains("false schema"),
            "The bulleted block must use the friendly translation, not the raw validator jargon.");
    }

    [TestMethod]
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

        StringAssert.Contains(rendered, "current value: \"max\"",
            "The offending value should be shown so the user sees what they have.");
        StringAssert.Contains(rendered, "allowed values: low, medium, high, xhigh",
            "The permitted enum values should be listed so the user knows the valid options.");
        StringAssert.Contains(rendered, "(Local scope)",
            "settings.local.json should be labelled with its scope.");
    }

    [TestMethod]
    public void Format_UnenrichedError_RendersExactlyAsBefore()
    {
        // Errors without Value/AllowedValues (the common path) must not gain blank
        // detail lines — the enrichment is strictly additive.
        SchemaValidationError[] errors = [Make("/some/path", "minLength constraint failed")];

        string rendered = SchemaErrorMessages.Format(errors);

        Assert.IsFalse(rendered.Contains("current value:"),
            "No value line should appear when the error carries no Value.");
        Assert.IsFalse(rendered.Contains("allowed values:"),
            "No allowed-values line should appear when the error carries no AllowedValues.");
    }
}
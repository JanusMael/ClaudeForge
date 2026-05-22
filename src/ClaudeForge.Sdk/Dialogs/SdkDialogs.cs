namespace Bennewitz.Ninja.ClaudeForge.Sdk.Dialogs;

/// <summary>
/// SDK-produced <see cref="DialogMessage"/> templates for operations the
/// SDK has authoritative knowledge about — actual file paths it will
/// write, schema-violation context, save-completion summaries, etc.
/// </summary>
/// <remarks>
/// <para>
/// The SDK does not render dialogs.  These factories return
/// <see cref="DialogMessage"/> models that any host (Avalonia GUI, MCP
/// server formatting status into chat, future CLI) consumes uniformly.
/// </para>
/// <para>
/// As more dialog flows migrate to <see cref="DialogMessage"/>, prefer
/// adding them here rather than ad-hoc constructing dialog text in each
/// host — that way wording, path-rendering policy, and category choice
/// stay consistent across products and language bundles.
/// </para>
/// </remarks>
public static class SdkDialogs
{
    /// <summary>
    /// Build the "X file(s) written" confirmation that follows a successful
    /// <c>SaveAsync</c>.  Each file path is rendered as a click-to-copy
    /// path span; categorised as <see cref="DialogCategory.Information"/>.
    /// </summary>
    /// <param name="writtenPaths">Absolute paths of the files just written.</param>
    public static DialogMessage SaveSucceeded(IReadOnlyList<string> writtenPaths)
    {
        DialogMessageBuilder b = DialogMessage.Builder();
        if (writtenPaths.Count == 0)
        {
            b.Text("No changes to save.");
            return b.Build();
        }

        b.Text(writtenPaths.Count == 1
            ? "Settings saved to "
            : $"Settings saved to {writtenPaths.Count} files:\n");

        for (int i = 0; i < writtenPaths.Count; i++)
        {
            if (writtenPaths.Count > 1)
            {
                b.Text("• ");
            }

            b.Path(writtenPaths[i]);
            if (i < writtenPaths.Count - 1)
            {
                b.Text("\n");
            }
        }

        return b.Build();
    }

    /// <summary>
    /// Build the "save failed" alert content.  Categorised as
    /// <see cref="DialogCategory.Error"/> by the host; the failing path is
    /// surfaced as a copyable path span and the underlying error message
    /// follows on a new line.
    /// </summary>
    public static DialogMessage SaveFailed(string targetPath, string errorMessage)
    {
        return DialogMessage.Builder()
                            .Text("Failed to save ")
                            .Path(targetPath)
                            .Text(":\n\n")
                            .Text(errorMessage)
                            .Build();
    }

    /// <summary>
    /// Build the "schema validation failed" alert content.  Optionally
    /// renders a documentation hyperlink the user can click to learn what
    /// the violated rule means.
    /// </summary>
    public static DialogMessage SchemaValidationFailed(
        string fieldDescription,
        string validationMessage,
        string? docsUrl = null)
    {
        DialogMessageBuilder b = DialogMessage.Builder()
                                              .Text("The value for ")
                                              .Bold(fieldDescription)
                                              .Text(" is invalid:\n\n")
                                              .Text(validationMessage);
        if (!string.IsNullOrEmpty(docsUrl))
        {
            b.Text("\n\nSee ").Hyperlink("the schema reference", docsUrl).Text(" for the rule.");
        }

        return b.Build();
    }

    /// <summary>
    /// Build the "this product is not installed" banner content.  Used by
    /// the About / install-banner area; rendered as
    /// <see cref="DialogCategory.Information"/> with a hyperlink to the
    /// install instructions.
    /// </summary>
    public static DialogMessage NotInstalled(string productName, string installUrl)
    {
        return DialogMessage.Builder()
                            .Bold(productName)
                            .Text(" was not detected on this machine. See ")
                            .Hyperlink("install instructions", installUrl)
                            .Text(".")
                            .Build();
    }
}
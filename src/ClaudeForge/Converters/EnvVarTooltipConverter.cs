using System.Globalization;
using Avalonia.Data.Converters;

namespace Bennewitz.Ninja.ClaudeForge.Converters;

/// <summary>
/// Maps well-known environment variable names to brief descriptions of their purpose.
/// Returns null for variables that are not in the known set (no tooltip shown).
/// Used in the Environment editor's Name column.
/// </summary>
public sealed class EnvVarTooltipConverter : IValueConverter
{
    private static readonly Dictionary<string, string> Descriptions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // ── Anthropic / Claude auth ──────────────────────────────────────────
            ["ANTHROPIC_API_KEY"] =
                "API key sent as X-Api-Key header. When set, this key is used instead of your Claude Pro/Max/Team subscription even if you are logged in.",
            ["ANTHROPIC_AUTH_TOKEN"] =
                "Custom value for the Authorization header (prefixed with 'Bearer '). Alternative to ANTHROPIC_API_KEY.",
            ["ANTHROPIC_BASE_URL"] =
                "Override the Anthropic API base URL to route requests through a proxy or gateway.",
            ["ANTHROPIC_BETAS"] =
                "Comma-separated list of additional anthropic-beta header values to include in API requests.",
            ["ANTHROPIC_CUSTOM_HEADERS"] =
                "Custom headers to add to requests (Name: Value format, newline-separated for multiple headers).",
            ["ANTHROPIC_MODEL"] = "Name of the model to use.",
            ["ANTHROPIC_SMALL_FAST_MODEL"] = "[DEPRECATED] Name of Haiku-class model for background tasks.",

            // ── Anthropic model overrides ────────────────────────────────────────
            ["ANTHROPIC_DEFAULT_SONNET_MODEL"] = "Default Sonnet-class model to use.",
            ["ANTHROPIC_DEFAULT_HAIKU_MODEL"] = "Default Haiku-class model to use.",
            ["ANTHROPIC_DEFAULT_OPUS_MODEL"] = "Default Opus-class model to use.",
            ["ANTHROPIC_CUSTOM_MODEL_OPTION"] = "Model ID to add as a custom entry in the /model picker.",

            // ── API / network ────────────────────────────────────────────────────
            ["API_TIMEOUT_MS"] =
                "Timeout for API requests in milliseconds (default: 600000 / 10 minutes; max: 2147483647).",
            ["CLAUDE_CONFIG_DIR"] = "Override the configuration directory (default: ~/.claude).",
            ["CLAUDE_CODE_API_KEY_HELPER"] =
                "Path to a helper binary that writes an API key to stdout; used for dynamic/rotating key retrieval.",
            ["CLAUDE_CODE_MAX_RETRIES"] = "Override the number of times to retry failed API requests (default: 10).",
            ["CLAUDE_CODE_MAX_OUTPUT_TOKENS"] =
                "Set the maximum number of output tokens for most Claude Code API requests.",
            ["CLAUDE_CODE_MAX_CONTEXT_TOKENS"] =
                "Override the context window size Claude Code assumes for the active model.",
            ["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] =
                "Disables telemetry, update checks, error reporting, and feedback commands — equivalent to setting DISABLE_AUTOUPDATER, DISABLE_TELEMETRY, DISABLE_ERROR_REPORTING, and DISABLE_FEEDBACK_COMMAND.",

            // ── Disable flags ────────────────────────────────────────────────────
            ["DISABLE_AUTOUPDATER"] = "Disable automatic update checks.",
            ["DISABLE_ERROR_REPORTING"] = "Disable automatic error reporting to Anthropic.",
            ["DISABLE_FEEDBACK_COMMAND"] = "Disable the /feedback command.",
            ["DISABLE_TELEMETRY"] = "Disable usage telemetry data collection.",

            // ── AWS / Bedrock ────────────────────────────────────────────────────
            ["CLAUDE_CODE_USE_BEDROCK"] = "Set to '1' to route Claude Code API requests through AWS Bedrock.",
            ["ANTHROPIC_BEDROCK_BASE_URL"] =
                "Override the Bedrock endpoint URL. Use for custom Bedrock endpoints or LLM gateways.",
            ["AWS_BEARER_TOKEN_BEDROCK"] = "Bedrock API key for authentication.",
            ["AWS_ACCESS_KEY_ID"] = "AWS access key ID; required when routing through AWS Bedrock.",
            ["AWS_SECRET_ACCESS_KEY"] = "AWS secret access key; required when routing through AWS Bedrock.",
            ["AWS_REGION"] = "AWS region for Bedrock API calls (e.g. 'us-east-1').",
            ["CLAUDE_CODE_SKIP_BEDROCK_AUTH"] = "Skip AWS authentication for Bedrock.",
            ["CLAUDE_CODE_USE_MANTLE"] = "Use the Bedrock Mantle endpoint.",
            ["ANTHROPIC_BEDROCK_MANTLE_BASE_URL"] = "Override the Bedrock Mantle endpoint URL.",
            ["CLAUDE_CODE_SKIP_MANTLE_AUTH"] = "Skip AWS authentication for Bedrock Mantle.",

            // ── Google Cloud / Vertex AI ─────────────────────────────────────────
            ["CLAUDE_CODE_USE_VERTEX"] = "Set to '1' to route Claude Code API requests through Google Vertex AI.",
            ["ANTHROPIC_VERTEX_BASE_URL"] = "Override the Vertex AI endpoint URL.",
            ["ANTHROPIC_VERTEX_PROJECT_ID"] = "GCP project ID for Vertex AI. Required when using Google Vertex AI.",
            ["GOOGLE_APPLICATION_CREDENTIALS"] =
                "Path to a Google Cloud service account JSON key file; used for Vertex AI authentication.",
            ["GOOGLE_CLOUD_PROJECT"] = "Google Cloud project ID used for Vertex AI API calls.",
            ["GCLOUD_PROJECT"] = "Alias for GOOGLE_CLOUD_PROJECT; Google Cloud project ID.",
            ["CLAUDE_CODE_SKIP_VERTEX_AUTH"] = "Skip Google authentication for Vertex AI.",

            // ── Microsoft Foundry ────────────────────────────────────────────────
            ["CLAUDE_CODE_USE_FOUNDRY"] = "Use Microsoft Azure Foundry as the API provider.",
            ["ANTHROPIC_FOUNDRY_BASE_URL"] = "Full base URL for the Foundry resource.",
            ["ANTHROPIC_FOUNDRY_RESOURCE"] =
                "Foundry resource name. Required if ANTHROPIC_FOUNDRY_BASE_URL is not set.",
            ["ANTHROPIC_FOUNDRY_API_KEY"] = "API key for Microsoft Foundry authentication.",
            ["CLAUDE_CODE_SKIP_FOUNDRY_AUTH"] = "Skip Azure authentication for Microsoft Foundry.",

            // ── Bash / shell ─────────────────────────────────────────────────────
            ["BASH_DEFAULT_TIMEOUT_MS"] =
                "Default timeout for long-running bash commands (default: 120000 / 2 minutes).",
            ["BASH_MAX_TIMEOUT_MS"] =
                "Maximum timeout the model can set for long-running bash commands (default: 600000 / 10 minutes).",
            ["BASH_MAX_OUTPUT_LENGTH"] =
                "Maximum number of characters in bash outputs before they are middle-truncated.",
            ["CLAUDE_CODE_SHELL"] = "Override automatic shell detection for running commands.",
            ["CLAUDE_CODE_SHELL_PREFIX"] = "Command prefix to wrap all bash commands.",
            ["CLAUDE_CODE_GIT_BASH_PATH"] = "Windows only: path to the Git Bash executable (bash.exe).",
            ["CLAUDE_ENV_FILE"] =
                "Path to a shell script Claude Code sources before each Bash command — use to persist virtualenv or conda activation across commands.",
            ["CLAUDECODE"] =
                "Set to 1 in shell environments Claude Code spawns. Check this to detect when a script is running inside a Claude-spawned shell.",

            // ── Context / thinking ───────────────────────────────────────────────
            ["MAX_THINKING_TOKENS"] = "Control the thinking budget for extended-thinking models.",
            ["CLAUDE_CODE_EFFORT_LEVEL"] =
                "Set effort level for supported models: low, medium, high, xhigh, max, or auto.",
            ["CLAUDE_CODE_DISABLE_THINKING"] =
                "Set to '1' to force-disable extended thinking regardless of model support.",
            ["CLAUDE_CODE_DISABLE_ADAPTIVE_THINKING"] =
                "Set to '1' to disable adaptive reasoning on Opus 4.6 and Sonnet 4.6.",
            ["CLAUDE_AUTOCOMPACT_PCT_OVERRIDE"] =
                "Set the percentage of context capacity (1–100) at which auto-compaction triggers.",

            // ── Plugins / MCP ────────────────────────────────────────────────────
            ["CLAUDE_CODE_PLUGIN_GIT_TIMEOUT_MS"] =
                "Timeout in milliseconds for git operations when installing or updating plugins (default: 120000).",
            ["CLAUDE_CODE_PLUGIN_CACHE_DIR"] = "Override the plugins root directory.",
            ["CLAUDE_CODE_PLUGIN_SEED_DIR"] =
                "Path to one or more read-only plugin seed directories (colon-separated on Unix, semicolon on Windows).",

            // ── TLS / proxy ──────────────────────────────────────────────────────
            ["HTTP_PROXY"] = "Proxy server URL used for outbound HTTP traffic.",
            ["HTTPS_PROXY"] = "Proxy server URL used for outbound HTTPS traffic.",
            ["NO_PROXY"] = "Comma-separated list of hosts/domains to bypass proxy routing.",
            ["CLAUDE_CODE_CERT_STORE"] =
                "Comma-separated list of CA certificate sources for TLS connections (default: bundled,system).",
            ["CLAUDE_CODE_CLIENT_CERT"] = "Path to client certificate file for mTLS authentication.",
            ["CLAUDE_CODE_CLIENT_KEY"] = "Path to client private key file for mTLS authentication.",
            ["CLAUDE_CODE_CLIENT_KEY_PASSPHRASE"] = "Passphrase for an encrypted CLAUDE_CODE_CLIENT_KEY.",
            ["CLAUDE_CODE_PROXY_RESOLVES_HOSTS"] =
                "Set to '1' to allow the proxy to perform DNS resolution instead of the caller.",

            // ── OpenTelemetry ────────────────────────────────────────────────────
            ["CLAUDE_CODE_ENABLE_TELEMETRY"] =
                "Set to '1' to enable OpenTelemetry data collection for metrics and logging.",
            ["CLAUDE_CODE_OTEL_FLUSH_TIMEOUT_MS"] =
                "Timeout in milliseconds for flushing pending OpenTelemetry spans (default: 5000).",
            ["CLAUDE_CODE_OTEL_SHUTDOWN_TIMEOUT_MS"] =
                "Timeout in milliseconds for the OTel exporter to finish on shutdown (default: 2000).",

            // ── Node.js runtime ──────────────────────────────────────────────────
            ["NODE_PATH"] = "Additional directories Node.js searches when resolving require() module paths.",
            ["NODE_OPTIONS"] =
                "Default command-line options passed to every Node.js process (e.g. --max-old-space-size).",
            ["NODE_ENV"] = "Execution environment name; typically 'development', 'test', or 'production'.",
            ["NPM_TOKEN"] = "Authentication token for publishing packages to an npm registry.",

            // ── Shell / OS fundamentals ──────────────────────────────────────────
            ["PATH"] = "Ordered list of directories searched when executing commands by name (no full path required).",
            ["HOME"] = "Current user's home directory (Unix / macOS).",
            ["USERPROFILE"] = "Current user's home directory (Windows).",
            ["HOMEPATH"] = "Path portion of the current user's home directory (Windows legacy).",
            ["SHELL"] = "Path to the default shell executable (e.g. /bin/bash, /bin/zsh).",
            ["COMSPEC"] = "Path to the default command interpreter on Windows (typically cmd.exe).",
            ["WSLENV"] =
                "Windows ↔ WSL environment-variable bridge (Windows only). A colon-separated list of variable names to share between the Windows host and WSL, each optionally followed by a flag: /p (translate value as a single path), /l (treat value as a colon-separated path list), /u (Windows→WSL direction only), /w (WSL→Windows direction only). Example: ANTHROPIC_API_KEY/u:CLAUDE_CONFIG_DIR/p passes the API key into WSL and translates the config-dir path on the way in.",
            ["TEMP"] = "Temporary file storage directory.",
            ["TMP"] = "Temporary file storage directory (alias for TEMP).",
            ["TMPDIR"] = "Temporary file storage directory (Unix / macOS).",
            ["CLAUDE_CODE_TMPDIR"] = "Override the temp directory used for Claude Code's internal temp files.",
        };

    /// <summary>
    /// Returns the description for the given environment variable name, or <c>null</c>
    /// when the name is not in the known set. Used by the detail pane to show a
    /// description label directly below the variable name.
    /// </summary>
    public static string? GetDescription(string? name)
    {
        return name != null && Descriptions.TryGetValue(name, out string? desc) ? desc : null;
    }

    /// <summary>
    /// Returns the known description for the variable name, or falls back to the name
    /// itself so the Name column always shows something useful on hover.
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string name)
        {
            return null;
        }

        return Descriptions.TryGetValue(name, out string? desc) ? desc : name;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
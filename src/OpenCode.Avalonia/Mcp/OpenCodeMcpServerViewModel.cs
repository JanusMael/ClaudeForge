using Bennewitz.Ninja.OpenCode.Avalonia.Editing;
using Bennewitz.Ninja.OpenCode.Avalonia.Localization;
using Bennewitz.Ninja.OpenCode.Sdk.Mcp;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.OpenCode.Avalonia.Mcp;

/// <summary>
/// Whether a server starts. Three states, because the key is optional and absent is not false.
/// </summary>
public enum OpenCodeMcpEnabledState
{
    /// <summary>The key is absent — OpenCode's own default applies.</summary>
    Unset,

    /// <summary><c>"enabled": true</c>.</summary>
    Enabled,

    /// <summary><c>"enabled": false</c>.</summary>
    Disabled,
}

/// <summary>
/// What a remote server's <c>oauth</c> key says.
/// </summary>
public enum OpenCodeMcpOAuthMode
{
    /// <summary>Key absent — OpenCode auto-detects.</summary>
    AutoDetect,

    /// <summary>Literal <c>false</c> — auto-detection off.</summary>
    Disabled,

    /// <summary>An object, whether populated or empty.</summary>
    Configured,
}

/// <summary>
/// One entry of the <c>mcp</c> map.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>Both union arms are held at once, never swapped.</b> Flipping a server local↔remote leaves
/// the other arm's fields in memory, so a user who switches to look at the remote form and back
/// still has their command, cwd and environment. This is the one behaviour the plan's recommended
/// template genuinely does have, and it matters more here than there: retyping an argv and a
/// dozen environment variables is not a small loss.
/// </para>
/// <para>
/// ⚠ <b>An unrecognised entry is not editable, and that is the feature.</b> Its value is held
/// verbatim and written back untouched. Offering fields for it would mean guessing at a shape this
/// build does not know, and saving the guess.
/// </para>
/// </remarks>
public sealed partial class OpenCodeMcpServerViewModel : ObservableObject
{
    /// <summary>The three enabled states, for binding a selector.</summary>
    public static IReadOnlyList<OpenCodeMcpEnabledState> EnabledStates { get; } =
        [OpenCodeMcpEnabledState.Unset, OpenCodeMcpEnabledState.Enabled, OpenCodeMcpEnabledState.Disabled];

    /// <summary>The three OAuth modes, for binding a selector.</summary>
    public static IReadOnlyList<OpenCodeMcpOAuthMode> OAuthModes { get; } =
        [OpenCodeMcpOAuthMode.AutoDetect, OpenCodeMcpOAuthMode.Disabled, OpenCodeMcpOAuthMode.Configured];

    private readonly Action<OpenCodeMcpServerViewModel>? _onRemove;

    /// <summary>Fields and raw payload this editor does not surface, replayed on save.</summary>
    private IReadOnlyList<KeyValuePair<string, object?>> _extras = [];

    private object? _raw;

    /// <summary>Creates an entry named <paramref name="name"/>.</summary>
    /// <param name="name">The server key.</param>
    /// <param name="onRemove">Invoked by <see cref="RemoveCommand"/>.</param>
    public OpenCodeMcpServerViewModel(
        string name,
        Action<OpenCodeMcpServerViewModel>? onRemove = null)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _onRemove = onRemove;
    }

    /// <summary>The server key, exactly as written.</summary>
    [ObservableProperty] private string _name;

    /// <summary>Which arm this entry is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocal))]
    [NotifyPropertyChangedFor(nameof(IsRemote))]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    [NotifyPropertyChangedFor(nameof(IsOpaque))]
    private OpenCodeMcpKind _kind = OpenCodeMcpKind.Local;

    /// <summary>True when the local fields apply.</summary>
    public bool IsLocal => Kind == OpenCodeMcpKind.Local;

    /// <summary>True when the remote fields apply.</summary>
    public bool IsRemote => Kind == OpenCodeMcpKind.Remote;

    /// <summary>True when this entry has editable fields at all.</summary>
    public bool IsEditable => Kind != OpenCodeMcpKind.Unrecognised;

    /// <summary>True when this entry is held verbatim rather than interpreted.</summary>
    public bool IsOpaque => Kind == OpenCodeMcpKind.Unrecognised;

    /// <summary>Whether the server starts.</summary>
    [ObservableProperty] private OpenCodeMcpEnabledState _enabledState = OpenCodeMcpEnabledState.Unset;

    /// <summary>Request timeout in milliseconds, or null when unstated.</summary>
    [ObservableProperty] private long? _timeoutMs;

    // ── Local arm ────────────────────────────────────────────────────────────

    /// <summary>The command and its arguments, in order.</summary>
    public OpenCodeStringListViewModel Command { get; } = new();

    /// <summary>Working directory; relative paths resolve from the workspace.</summary>
    [ObservableProperty] private string _workingDirectory = string.Empty;

    /// <summary>Environment variables for the server process.</summary>
    public OpenCodePairListViewModel Environment { get; } = new();

    // ── Remote arm ───────────────────────────────────────────────────────────

    /// <summary>The endpoint URL.</summary>
    [ObservableProperty] private string _url = string.Empty;

    /// <summary>Headers sent with each request.</summary>
    public OpenCodePairListViewModel Headers { get; } = new();

    /// <summary>What the <c>oauth</c> key says.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOAuthFields))]
    private OpenCodeMcpOAuthMode _oauthMode = OpenCodeMcpOAuthMode.AutoDetect;

    /// <summary>True when the OAuth detail fields apply.</summary>
    public bool ShowOAuthFields => OauthMode == OpenCodeMcpOAuthMode.Configured;

    /// <summary>OAuth client id; absent means dynamic registration is attempted.</summary>
    [ObservableProperty] private string _oauthClientId = string.Empty;

    /// <summary>
    /// OAuth client secret.
    /// </summary>
    /// <remarks>
    /// ⚠ A real credential, living in a plain-text config file — that is OpenCode's design, not
    /// this editor's choice. What this editor can avoid is making it worse: the view masks the box
    /// by default so the value does not end up in a screen share or a screenshot, and nothing here
    /// is ever written to a log.
    /// </remarks>
    [ObservableProperty] private string _oauthClientSecret = string.Empty;

    /// <summary>Scopes to request.</summary>
    [ObservableProperty] private string _oauthScope = string.Empty;

    /// <summary>Local callback port; shorthand for the redirect URI.</summary>
    [ObservableProperty] private long? _oauthCallbackPort;

    /// <summary>Full redirect URI; takes precedence over the port.</summary>
    [ObservableProperty] private string _oauthRedirectUri = string.Empty;

    // ── Opaque arm ───────────────────────────────────────────────────────────

    /// <summary>
    /// Why this entry is not editable, for the banner. Empty when it is editable.
    /// </summary>
    [ObservableProperty] private string _opaqueNotice = string.Empty;

    /// <summary>Remove this server from the map.</summary>
    [RelayCommand]
    private void Remove() => _onRemove?.Invoke(this);

    /// <summary>Populate from a parsed entry.</summary>
    internal void Load(OpenCodeMcpServer server)
    {
        Kind = server.Kind;
        _extras = server.Extras;
        _raw = server.Raw;

        EnabledState = server.Enabled switch
        {
            true => OpenCodeMcpEnabledState.Enabled,
            false => OpenCodeMcpEnabledState.Disabled,
            null => OpenCodeMcpEnabledState.Unset,
        };
        TimeoutMs = server.TimeoutMs;

        Command.Reset(server.Command);
        WorkingDirectory = server.WorkingDirectory ?? string.Empty;
        Environment.Reset(server.Environment);

        Url = server.Url ?? string.Empty;
        Headers.Reset(server.Headers);

        OauthMode = server switch
        {
            { OAuthDisabled: true } => OpenCodeMcpOAuthMode.Disabled,
            { OAuth: not null } => OpenCodeMcpOAuthMode.Configured,
            var _ => OpenCodeMcpOAuthMode.AutoDetect,
        };

        OpenCodeMcpOAuth oauth = server.OAuth ?? new OpenCodeMcpOAuth();
        OauthClientId = oauth.ClientId ?? string.Empty;
        OauthClientSecret = oauth.ClientSecret ?? string.Empty;
        OauthScope = oauth.Scope ?? string.Empty;
        OauthCallbackPort = oauth.CallbackPort;
        OauthRedirectUri = oauth.RedirectUri ?? string.Empty;

        OpaqueNotice = server.Kind == OpenCodeMcpKind.Unrecognised
            ? Strings.McpOpaqueNotice
            : string.Empty;
    }

    /// <summary>The model form of this entry.</summary>
    /// <remarks>
    /// ⚠ Writes only the arm <see cref="Kind"/> selects, even though both are populated. The other
    /// arm stays in memory so a switch is reversible; emitting it would produce a config OpenCode
    /// rejects, since both variants set <c>additionalProperties: false</c>.
    /// </remarks>
    internal OpenCodeMcpServer ToModel()
    {
        bool? enabled = EnabledState switch
        {
            OpenCodeMcpEnabledState.Enabled => true,
            OpenCodeMcpEnabledState.Disabled => false,
            var _ => null,
        };

        if (Kind == OpenCodeMcpKind.Unrecognised)
        {
            return new OpenCodeMcpServer { Kind = Kind, Raw = _raw };
        }

        if (Kind == OpenCodeMcpKind.EnabledOverride)
        {
            // The schema's third arm forbids every other key, so an unset value cannot round-trip
            // as "absent" here — the arm exists only to state one.
            return new OpenCodeMcpServer
            {
                Kind = Kind,
                Enabled = enabled ?? true,
            };
        }

        if (Kind == OpenCodeMcpKind.Local)
        {
            return new OpenCodeMcpServer
            {
                Kind = OpenCodeMcpKind.Local,
                Command = Command.ToValues(),
                WorkingDirectory = WorkingDirectory,
                Environment = Environment.ToPairs(),
                Enabled = enabled,
                TimeoutMs = TimeoutMs,
                Extras = _extras,
            };
        }

        return new OpenCodeMcpServer
        {
            Kind = OpenCodeMcpKind.Remote,
            Url = Url,
            Headers = Headers.ToPairs(),
            OAuthDisabled = OauthMode == OpenCodeMcpOAuthMode.Disabled,
            OAuth = OauthMode == OpenCodeMcpOAuthMode.Configured
                ? new OpenCodeMcpOAuth(
                    ClientId: NullIfBlank(OauthClientId),
                    ClientSecret: NullIfBlank(OauthClientSecret),
                    Scope: NullIfBlank(OauthScope),
                    CallbackPort: OauthCallbackPort,
                    RedirectUri: NullIfBlank(OauthRedirectUri))
                : null,
            Enabled = enabled,
            TimeoutMs = TimeoutMs,
            Extras = _extras,
        };
    }

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

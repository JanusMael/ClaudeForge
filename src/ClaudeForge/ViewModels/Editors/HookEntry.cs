using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.JsonHelpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

public enum HookCommandType
{
    Command,
    Prompt,
    Url
}

/// <summary>
/// Editable key/value pair for a hook's <c>headers</c> dictionary
/// (URL-typed hooks).  Mirrors <see cref="EnvVarEntry"/>'s shape — both
/// are bound via <c>DataGrid</c> with two text columns.
/// </summary>
public partial class HookHeaderEntry : ObservableObject
{
    [ObservableProperty] private string _key = string.Empty;
    [ObservableProperty] private string _value = string.Empty;
}

/// <summary>Display info for a single hook command type — value + human description.</summary>
public sealed record HookCommandTypeInfo(HookCommandType Value, string Description)
{
    public string DisplayName => Value.ToString();
    public string AccessibleName => $"{DisplayName}: {Description}";
}

/// <summary>A single hook within an event group.</summary>
public partial class HookEntry : ObservableObject
{
    /// <summary>All valid command types with descriptions — used as ItemsSource in the Type ComboBox.</summary>
    public static readonly IReadOnlyList<HookCommandTypeInfo> CommandTypeInfos =
    [
        new(HookCommandType.Command, "Run a shell command"),
        new(HookCommandType.Prompt, "Inject text into Claude's context"),
        new(HookCommandType.Url, "Open a URL in the default browser"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatcherIsValid))]
    [NotifyPropertyChangedFor(nameof(HasValidationWarning))]
    private string _matcher = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedCommandTypeInfo))]
    [NotifyPropertyChangedFor(nameof(ShowHttpFields))]
    private HookCommandType _commandType = HookCommandType.Command;

    /// <summary>
    ///visibility gate for the headers + allowedEnvVars
    /// per-row editor.  The Claude Code schema only defines those fields
    /// on the URL ("http") hook variant, so the editor only surfaces
    /// them for that <see cref="CommandType"/>.  The data still
    /// round-trips on other types via the normal save path — this just
    /// hides the editing affordance to keep the UI uncluttered.
    /// </summary>
    public bool ShowHttpFields => CommandType == HookCommandType.Url;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasValidationWarning))]
    private string _commandValue = string.Empty;

    /// <summary>
    /// typed timeout (seconds).  <see langword="null"/>
    /// means "not set" — CLI applies its own default.
    /// </summary>
    [ObservableProperty] private int? _timeout;

    /// <summary>
    /// editable HTTP headers for URL-typed hooks.
    /// Hidden in the UI for command/prompt hooks (validity is the
    /// schema's concern; the editor doesn't gate emission on type to
    /// preserve user data through type-flips).
    /// </summary>
    public ObservableCollection<HookHeaderEntry> Headers { get; } = new();

    /// <summary>
    /// editable allowed-env-vars for URL-typed hooks.
    /// Names listed here may be interpolated into <see cref="Headers"/>
    /// values via <c>${VARNAME}</c>.
    /// </summary>
    public ObservableCollection<string> AllowedEnvVars { get; } = new();

    [ObservableProperty] private string _newHeaderKey = string.Empty;
    [ObservableProperty] private string _newHeaderValue = string.Empty;
    [ObservableProperty] private string _newAllowedEnvVar = string.Empty;

    /// <summary>
    /// data-loss prevention: when FromJson encounters a hook whose
    /// <c>type</c> field is not one of the editor's known variants
    /// (<c>command</c> / <c>prompt</c> / <c>url</c>), the original JsonObject
    /// is stashed here verbatim so that ToJson can round-trip it unchanged.
    /// Without this, hooks of type <c>agent</c> or <c>http</c> (both valid in
    /// the Claude Code schema) were silently downcast to <c>command</c> with
    /// an empty value, then either dropped (filtered by HookEventGroup.ToJson)
    /// or written back as schema-invalid <c>{"type":"command"}</c> entries.
    /// </summary>
    /// <remarks>
    /// When non-null, the editor surfaces this hook as read-only — the user
    /// cannot edit unsupported types in the UI but the data survives intact.
    /// Future expansion of the editor to support agent/http natively can
    /// retire this field per type as the editor learns to render them.
    /// </remarks>
    private JsonObject? _opaqueJson;

    /// <summary>
    /// True when this hook has a <c>type</c> the editor doesn't natively support
    /// (e.g. <c>agent</c>, <c>http</c>) and is being preserved verbatim. UI
    /// surfaces it read-only — the user can delete it but not edit fields.
    /// </summary>
    public bool IsOpaque => _opaqueJson is not null;

    /// <summary>
    /// per-hook fields the editor doesn't render natively for
    /// known types (<c>timeout</c>, <c>async</c>, <c>statusMessage</c>,
    /// <c>model</c>, <c>headers</c>, <c>allowedEnvVars</c>). Captured by
    /// FromJson; replayed by ToJson so save round-trips don't drop them.
    /// Distinct from <see cref="_opaqueJson"/>: opaque preserves the
    /// whole-entry-verbatim case (unknown type); _extraFields preserves
    /// unknown SUB-FIELDS within a known type.
    /// </summary>
    private readonly JsonObject _extraFields = new();

    /// <summary>
    /// Bridge for the SDK-backed load path.  The SDK promotes
    /// <c>timeout</c>, <c>headers</c>, and <c>allowedEnvVars</c> to typed
    /// properties on its <c>HookEvent</c> record (Stop B); <em>this</em>
    /// editor mirror also surfaces them as typed UI affordances (Stop C).
    /// SDK consumers pass typed values directly via the sibling
    /// <see cref="IngestTypedFields"/> below; <see cref="IngestPreservedFields"/>
    /// covers only the fields the SDK still doesn't model (async,
    /// statusMessage, model, etc.).
    /// </summary>
    internal void IngestPreservedFields(JsonObject? preserved)
    {
        if (preserved is null)
        {
            return;
        }

        foreach (KeyValuePair<string, JsonNode?> kv in preserved)
        {
            _extraFields[kv.Key] = kv.Value?.DeepClone();
        }
    }

    /// <summary>
    /// Apply typed Stop-B/C fields supplied by the SDK record so the
    /// editor's typed UI is populated on first load.  Replaces the
    /// transitional re-bundle hack the editor used while
    /// <see cref="HookEntry"/> only had <c>_extraFields</c>.
    /// </summary>
    internal void IngestTypedFields(
        int? timeout,
        IReadOnlyDictionary<string, string>? headers,
        IReadOnlyList<string>? allowedEnvVars)
    {
        if (timeout is { } t)
        {
            Timeout = t;
        }

        if (headers is { Count: > 0 } hs)
        {
            foreach ((string k, string v) in hs)
            {
                Headers.Add(new HookHeaderEntry { Key = k, Value = v });
            }
        }

        if (allowedEnvVars is { Count: > 0 } av)
        {
            foreach (string name in av)
            {
                AllowedEnvVars.Add(name);
            }
        }
    }

    /// <summary>
    /// Stash an opaque inner JsonObject (deep-
    /// cloned) supplied by the SDK-backed load path so the editor surfaces
    /// the hook as read-only and <see cref="ToJson"/> emits it verbatim on
    /// save.  Mirrors the legacy <see cref="FromJson"/> path's _opaqueJson
    /// stash for SDK consumers.  When non-null, also sets a synthetic
    /// <see cref="CommandValue"/> showing the original raw type so the
    /// user sees something meaningful in the grid (matches FromJson's
    /// "(agent hook — preserved as-is)" prefix).
    /// </summary>
    internal void IngestOpaqueJson(JsonObject? opaque)
    {
        if (opaque is null)
        {
            return;
        }

        _opaqueJson = (JsonObject)opaque.DeepClone();

        // Surface the opaque type-tag in the grid so the user sees what's
        // there even though it can't be edited.  Mirrors FromJson's
        // synthetic CommandValue for the same legacy-path case.
        string rawType = opaque["type"].AsStringOrNull()?.ToLowerInvariant() ?? "unknown";
        CommandValue = $"({rawType} hook — preserved as-is)";
    }

    // -----------------------------------------------------------------------
    // Validation
    // -----------------------------------------------------------------------

    /// <summary>
    /// True when Matcher is valid: either the wildcard <c>*</c>, a known tool name, or
    /// empty/whitespace (blank = not-yet-filled, treated as a warning rather than an error
    /// so the user can type before we highlight problems).
    /// </summary>
    public bool MatcherIsValid =>
        string.IsNullOrWhiteSpace(Matcher)
        || Matcher == "*"
        || PermissionRuleViewModel.KnownToolNames.Contains(Matcher);

    /// <summary>
    /// True when this hook has an empty Matcher or an empty CommandValue — the hook
    /// will be silently skipped during serialisation and will not fire.
    /// </summary>
    public bool HasValidationWarning =>
        string.IsNullOrWhiteSpace(Matcher) || string.IsNullOrWhiteSpace(CommandValue);

    /// <summary>Typed ComboBox binding — keeps <see cref="CommandType"/> as the source of truth.</summary>
    public HookCommandTypeInfo? SelectedCommandTypeInfo
    {
        get => CommandTypeInfos.FirstOrDefault(i => i.Value == CommandType);
        set
        {
            if (value == null || value.Value == CommandType)
            {
                return;
            }

            CommandType = value.Value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Parse a single inner hook object. Accepts either the preferred shape
    /// <c>{"type": "command", "command": "..."}</c> or a legacy flat shape where
    /// matcher/command/prompt/url all live on the same object.
    /// </summary>
    public static HookEntry FromJson(JsonObject obj)
    {
        HookEntry entry = new()
        {
            Matcher = obj["matcher"].AsStringOrNull() ?? string.Empty,
        };

        // Explicit "type" field (preferred shape): use it to pick which value field to read.
        string? explicitType = obj["type"].AsStringOrNull();
        if (!string.IsNullOrEmpty(explicitType))
        {
            // data-loss prevention. The editor only natively renders
            // command / prompt / url hooks. The Claude Code schema also allows
            // `agent` and `http` (and may add more types in the future).
            // Previously, anything unrecognised was silently downcast to Command
            // with an empty value, then dropped on the next save through this
            // editor — losing the user's data.
            //
            // Stash the original JsonObject so ToJson can emit it verbatim and
            // round-trip preserves the user's data even when the editor doesn't
            // know how to render it.
            string lower = explicitType.ToLowerInvariant();
            if (lower is not ("command" or "prompt" or "url"))
            {
                entry._opaqueJson = (JsonObject)obj.DeepClone();
                entry.CommandType = HookCommandType.Command;
                // Surface the opaque type in CommandValue so the user can see
                // *something* in the UI grid; HasValidationWarning treats it as
                // populated so it isn't dropped by HookEventGroup.ToJson's
                // empty-value filter.
                entry.CommandValue = $"({lower} hook — preserved as-is)";
                return entry;
            }

            entry.CommandType = lower switch
            {
                "prompt" => HookCommandType.Prompt,
                "url" => HookCommandType.Url,
                var _ => HookCommandType.Command,
            };
            string valueKey = entry.CommandType switch
            {
                HookCommandType.Prompt => "prompt",
                HookCommandType.Url => "url",
                var _ => "command",
            };
            entry.CommandValue = obj[valueKey].AsStringOrNull() ?? string.Empty;

            // extract typed sub-fields (timeout / headers
            // / allowedEnvVars) into typed properties.  Remaining unknowns
            // (async / statusMessage / model / future schema additions)
            // continue to live in _extraFields and round-trip via ToJson.
            foreach (KeyValuePair<string, JsonNode?> kv in obj)
            {
                if (kv.Key == "matcher" || kv.Key == "type" || kv.Key == valueKey)
                {
                    continue;
                }

                switch (kv.Key)
                {
                    case "timeout":
                        if (kv.Value is JsonValue tv && tv.TryGetValue(out int t))
                        {
                            entry.Timeout = t;
                        }

                        continue;
                    case "headers":
                        if (kv.Value is JsonObject ho)
                        {
                            foreach ((string hk, JsonNode? hv) in ho)
                            {
                                if (hv is JsonValue jv && jv.TryGetValue(out string? hs))
                                {
                                    entry.Headers.Add(new HookHeaderEntry { Key = hk, Value = hs });
                                }
                            }
                        }

                        continue;
                    case "allowedEnvVars":
                        if (kv.Value is JsonArray av)
                        {
                            foreach (JsonNode? item in av)
                            {
                                if (item is JsonValue ev && ev.TryGetValue(out string? es))
                                {
                                    entry.AllowedEnvVars.Add(es);
                                }
                            }
                        }

                        continue;
                    default:
                        entry._extraFields[kv.Key] = kv.Value?.DeepClone();
                        break;
                }
            }

            return entry;
        }

        // No explicit "type": infer from whichever string-typed value field is present.
        // AsStringOrNull tolerates type-mismatched JSON (e.g. {"command": 42}) by treating
        // the field as absent, so the inference falls through to the next candidate.
        if (obj["command"].AsStringOrNull() is { } cmdStr)
        {
            entry.CommandType = HookCommandType.Command;
            entry.CommandValue = cmdStr;
        }
        else if (obj["prompt"].AsStringOrNull() is { } promptStr)
        {
            entry.CommandType = HookCommandType.Prompt;
            entry.CommandValue = promptStr;
        }
        else if (obj["url"].AsStringOrNull() is { } urlStr)
        {
            entry.CommandType = HookCommandType.Url;
            entry.CommandValue = urlStr;
        }

        return entry;
    }

    /// <summary>
    /// Emit the inner hook shape: <c>{"type": "command", "command": "..."}</c>.
    /// The outer <see cref="HookEventGroup"/> carries the matcher.
    /// </summary>
    /// <remarks>
    /// When <see cref="IsOpaque"/> is true, this method emits the original
    /// JsonObject verbatim (deep-cloned). This preserves hooks of types the
    /// editor doesn't natively render (e.g. <c>agent</c>, <c>http</c>) so that
    /// loading and saving the file does not silently destroy the user's data.
    /// </remarks>
    // ── per-row Add/Remove for Headers + AllowedEnvVars ──

    /// <summary>
    /// Append a new <see cref="HookHeaderEntry"/> from the bound input
    /// boxes.  No-op when the key is empty.  Duplicate keys are
    /// allowed at the editor level — the user might be staging a
    /// header rename — they collapse to a single entry on save (last
    /// write wins).
    /// </summary>
    [RelayCommand]
    private void AddHeader()
    {
        string key = (NewHeaderKey ?? string.Empty).Trim();
        if (key.Length == 0)
        {
            return;
        }

        Headers.Add(new HookHeaderEntry { Key = key, Value = NewHeaderValue ?? string.Empty });
        NewHeaderKey = string.Empty;
        NewHeaderValue = string.Empty;
    }

    /// <summary>Remove a single header row.  Bound to the per-row × button.</summary>
    [RelayCommand]
    private void RemoveHeader(HookHeaderEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        Headers.Remove(entry);
    }

    /// <summary>
    /// Append a new entry to <see cref="AllowedEnvVars"/>.  No-op when
    /// the input is empty / already present (env-var names are
    /// effectively a set; duplicates would just confuse the user).
    /// </summary>
    [RelayCommand]
    private void AddAllowedEnvVar()
    {
        string name = (NewAllowedEnvVar ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return;
        }

        if (AllowedEnvVars.Contains(name))
        {
            return;
        }

        AllowedEnvVars.Add(name);
        NewAllowedEnvVar = string.Empty;
    }

    /// <summary>Remove a single allowed-env-var row.  Bound to the per-row × button.</summary>
    [RelayCommand]
    private void RemoveAllowedEnvVar(string? name)
    {
        if (name is null)
        {
            return;
        }

        AllowedEnvVars.Remove(name);
    }

    public JsonObject ToJson()
    {
        // Opaque path: round-trip the original verbatim. Deep-clone so the
        // caller can mutate the result without affecting our stash.
        if (_opaqueJson is not null)
        {
            return (JsonObject)_opaqueJson.DeepClone();
        }

        JsonObject obj = new();
        string typeStr = CommandType switch
        {
            HookCommandType.Prompt => "prompt",
            HookCommandType.Url => "url",
            var _ => "command",
        };
        obj["type"] = typeStr;
        // Always emit the value-key (command / prompt / url) even
        // when CommandValue is empty.  The Claude Code schema enforces
        // minLength:1 on command, so an empty string IS schema-invalid — but
        // that's surfaced by the save-time validation banner, not silently
        // hidden.  The previous behaviour (omit the key when empty) made
        // 'add hook' produce no JSON change relative to baseline, so the
        // structural-diff save-button gate stayed disabled and the user
        // couldn't see they had unsaved work.  Emitting empty text fires
        // the diff and lets the user save (or get a clear validation error).
        obj[typeStr] = CommandValue ?? string.Empty;

        // mit typed sub-fields (timeout / headers /
        // allowedEnvVars) from typed properties.
        if (Timeout is { } t)
        {
            obj["timeout"] = t;
        }

        if (Headers.Count > 0)
        {
            JsonObject ho = new();
            foreach (HookHeaderEntry h in Headers)
            {
                if (!string.IsNullOrEmpty(h.Key))
                {
                    ho[h.Key] = h.Value;
                }
            }

            if (ho.Count > 0)
            {
                obj["headers"] = ho;
            }
        }

        if (AllowedEnvVars.Count > 0)
        {
            JsonArray av = new();
            foreach (string n in AllowedEnvVars)
            {
                if (!string.IsNullOrWhiteSpace(n))
                    // Cast to JsonNode? — JsonArray.Add<T>(T) is IL2026 under trim.
                {
                    av.Add((JsonNode?)JsonValue.Create(n));
                }
            }

            if (av.Count > 0)
            {
                obj["allowedEnvVars"] = av;
            }
        }

        // replay preserved sub-fields (async, statusMessage,
        // model, etc.). Typed properties (type + value-key + Stop-C fields)
        // win on collision; the preserved bag is the fallback for unknowns.
        foreach (KeyValuePair<string, JsonNode?> kv in _extraFields)
        {
            if (obj.ContainsKey(kv.Key))
            {
                continue;
            }

            obj[kv.Key] = kv.Value?.DeepClone();
        }

        return obj;
    }
}
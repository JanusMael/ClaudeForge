using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Bennewitz.Ninja.ClaudeForge.Core.JsonHelpers;
using Bennewitz.Ninja.ClaudeForge.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bennewitz.Ninja.ClaudeForge.ViewModels.Editors;

/// <summary>Display info for a single MCP transport type — value + human description.</summary>
public sealed record McpTransportInfo(string Value, string Description)
{
    public string AccessibleName => $"{Value}: {Description}";
}

/// <summary>
/// Observable wrapper for a single command-line argument.
/// Required so DataGrid can edit individual string values in-place.
/// </summary>
public partial class ArgItem : ObservableObject
{
    public ArgItem(string value)
    {
        _value = value;
    }

    [ObservableProperty] private string _value;
}

/// <summary>Represents a single MCP server entry in the editor.</summary>
public partial class McpServerEntry : ObservableObject
{
    /// <summary>All valid transport types with descriptions — used as ItemsSource in the Transport ComboBox.</summary>
    public static readonly IReadOnlyList<McpTransportInfo> TransportInfos =
    [
        new("stdio", "Subprocess — Claude spawns the server, communicates via stdin/stdout"),
        new("sse", "Server-Sent Events — connect to a running HTTP server"),
        new("http", "Streamable HTTP — direct HTTP requests (MCP spec 2025-03-26+)"),
    ];

    public McpServerEntry(string name)
    {
        Name = name;
        Args = [];
        Env = [];
        Headers = [];
    }

    [ObservableProperty] private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTransportInfo))]
    [NotifyPropertyChangedFor(nameof(CommandMissing))]
    [NotifyPropertyChangedFor(nameof(UrlInvalid))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    private string _type = "stdio"; // stdio | sse | http

    /// <summary>Typed ComboBox binding — keeps <see cref="Type"/> as the source of truth.</summary>
    public McpTransportInfo? SelectedTransportInfo
    {
        get => TransportInfos.FirstOrDefault(t => t.Value == Type);
        set
        {
            if (value == null || value.Value == Type)
            {
                return;
            }

            Type = value.Value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CommandMissing))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    private string? _command;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UrlInvalid))]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    private string? _url;

    [ObservableProperty] private string _newArg = string.Empty;
    [ObservableProperty] private string _newEnvKey = string.Empty;
    [ObservableProperty] private string _newEnvValue = string.Empty;
    [ObservableProperty] private string? _headersJson;

    /// <summary>
    /// Free-form description for the server. 2026-05-01 promoted from
    /// the opaque <c>_extraFields</c> stash to a typed property so SDK
    /// consumers and (eventual) GUI affordances can read/write it.
    /// No UI binding yet — round-trip only.
    /// </summary>
    [ObservableProperty] private string? _description;

    public ObservableCollection<ArgItem> Args { get; }
    public ObservableCollection<EnvVar> Env { get; }
    public ObservableCollection<EnvVar> Headers { get; }

    // -----------------------------------------------------------------------
    // Transport-level validation
    // -----------------------------------------------------------------------

    /// <summary>True when the transport is "stdio" but no command has been set.</summary>
    public bool CommandMissing =>
        Type == "stdio" && string.IsNullOrWhiteSpace(Command);

    /// <summary>True when the transport is "sse" or "http" but the URL is missing or not a valid http/https address.</summary>
    public bool UrlInvalid =>
        Type is "sse" or "http" &&
        (!Uri.TryCreate(Url, UriKind.Absolute, out Uri? u) ||
         u.Scheme is not ("http" or "https"));

    /// <summary>True when any transport-level validation constraint is violated.</summary>
    public bool HasValidationError => CommandMissing || UrlInvalid;

    /// <summary>Human-readable explanation of the current validation error, or empty when valid.</summary>
    public string ValidationMessage =>
        CommandMissing ? Strings.ValidationMcpCommandRequired :
        UrlInvalid ? Strings.ValidationMcpUrlInvalid :
        string.Empty;

    private readonly JsonObject _extraFields = new();

    /// <summary>
    ///SDK-backed load path to populate the
    /// editor's preserved-fields stash. The legacy JSON-backed
    /// <see cref="FromJson"/> loader populates <c>_extraFields</c>
    /// directly during parsing; the SDK path goes through
    /// <see cref="McpServersEditorViewModel.McpServerEntryFromSdk"/>,
    /// which projects an <c>SdkMcp.McpServer</c> record. That record's
    /// <c>PreservedFields</c> property carries the unknown JSON fields
    /// (e.g. <c>description</c>) that the SDK doesn't model. This method
    /// copies them into <c>_extraFields</c> so <see cref="ToJson"/>
    /// re-emits them.
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

    [RelayCommand]
    private void AddArg()
    {
        string t = NewArg.Trim();
        if (!string.IsNullOrEmpty(t))
        {
            Args.Add(new ArgItem(t));
            NewArg = string.Empty;
        }
    }

    [RelayCommand]
    private void RemoveArg(ArgItem arg)
    {
        Args.Remove(arg);
    }

    [RelayCommand]
    private void AddEnv()
    {
        string k = NewEnvKey.Trim();
        if (!string.IsNullOrEmpty(k) && !Env.Any(e => e.Key == k))
        {
            Env.Add(new EnvVar(k, NewEnvValue));
            NewEnvKey = string.Empty;
            NewEnvValue = string.Empty;
        }
    }

    [RelayCommand]
    private void RemoveEnv(EnvVar ev)
    {
        Env.Remove(ev);
    }

    private static readonly HashSet<string> _knownFields =
        ["type", "command", "args", "env", "url", "headers", "description"];

    public static McpServerEntry FromJson(string name, JsonObject obj)
    {
        McpServerEntry entry = new(name)
        {
            Type = obj["type"].AsStringOrNull() ?? "stdio",
            Command = obj["command"].AsStringOrNull(),
            Url = obj["url"].AsStringOrNull(),
            Description = obj["description"].AsStringOrNull(),
        };

        if (obj["args"] is JsonArray args)
        {
            foreach (JsonNode? a in args)
            {
                if (a is JsonValue jv && jv.TryGetValue(out string? s))
                {
                    entry.Args.Add(new ArgItem(s));
                }
            }
        }

        if (obj["env"] is JsonObject env)
        {
            foreach (KeyValuePair<string, JsonNode?> kv in env)
            {
                entry.Env.Add(new EnvVar(kv.Key, kv.Value.AsStringOrNull() ?? string.Empty));
            }
        }

        if (obj["headers"] is JsonObject headers)
        {
            foreach (KeyValuePair<string, JsonNode?> kv in headers)
            {
                entry.Headers.Add(new EnvVar(kv.Key, kv.Value.AsStringOrNull() ?? string.Empty));
            }
        }

        foreach (KeyValuePair<string, JsonNode?> kv in obj)
        {
            if (!_knownFields.Contains(kv.Key))
            {
                entry._extraFields[kv.Key] = kv.Value?.DeepClone();
            }
        }

        return entry;
    }

    public JsonObject ToJson()
    {
        JsonObject obj = new();
        obj["type"] = Type;
        if (!string.IsNullOrEmpty(Command))
        {
            obj["command"] = Command;
        }

        if (!string.IsNullOrEmpty(Url))
        {
            obj["url"] = Url;
        }

        if (Args.Count > 0)
        {
            JsonArray arr = new();
            foreach (ArgItem a in Args)
            {
                arr.Add((JsonNode?)JsonValue.Create(a.Value));
            }

            obj["args"] = arr;
        }

        if (Env.Count > 0)
        {
            JsonObject envObj = new();
            foreach (EnvVar e in Env)
            {
                envObj[e.Key] = e.Value;
            }

            obj["env"] = envObj;
        }

        if (Headers.Count > 0)
        {
            JsonObject headersObj = new();
            foreach (EnvVar h in Headers)
            {
                headersObj[h.Key] = h.Value;
            }

            obj["headers"] = headersObj;
        }

        // typed Description property emitted explicitly.
        if (!string.IsNullOrEmpty(Description))
        {
            obj["description"] = Description;
        }

        foreach (KeyValuePair<string, JsonNode?> kv in _extraFields)
        {
            // Typed properties win on key collision — the preserved bag
            // is the fallback for unknowns only.
            if (obj.ContainsKey(kv.Key))
            {
                continue;
            }

            obj[kv.Key] = kv.Value?.DeepClone();
        }

        return obj;
    }
}

public partial class EnvVar : ObservableObject
{
    public EnvVar(string key, string value)
    {
        Key = key;
        Value = value;
    }

    [ObservableProperty] private string _key;
    [ObservableProperty] private string _value;
}
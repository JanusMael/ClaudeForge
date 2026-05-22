# Localization Guide

This document explains how to add, modify, and translate user-visible strings in ClaudeForge.

---

## Table of Contents

1. [How it works](#how-it-works)
2. [Adding or changing a string](#adding-or-changing-a-string)
3. [Key naming conventions](#key-naming-conventions)
4. [Using strings in AXAML](#using-strings-in-axaml)
5. [Using strings in C# code](#using-strings-in-c-code)
6. [Adding a new language](#adding-a-new-language)
7. [Culture resolution at startup](#culture-resolution-at-startup)
8. [Regenerating Strings.Designer.cs](#regenerating-stringsdesignercs)
9. [Trimming and single-file builds](#trimming-and-single-file-builds)

---

## How it works

All user-visible strings live in a single `.resx` file:

```
src/ClaudeForge/Localization/
├── Strings.resx          ← master English strings (edit this)
├── Strings.Designer.cs   ← auto-generated strongly-typed wrapper (commit this)
└── Strings.zh-Hans.resx  ← example translation file (create per language)
```

The `Strings.resx` file is an embedded resource compiled into the assembly. The .NET
`ResourceManager` resolves the correct satellite assembly at runtime based on
`CultureInfo.CurrentUICulture`. If no satellite assembly matches the current culture,
it falls back to the base English strings.

`Strings.Designer.cs` is generated from `Strings.resx` by `ResXFileCodeGenerator`.
It exposes every key as a `public static string` property so that typos in key names
are caught at compile time rather than at runtime.

---

## Adding or changing a string

### Step 1 — Edit `Strings.resx`

Open `src/ClaudeForge/Localization/Strings.resx` and add a `<data>` element inside
the `<root>` block. Place it in the section that matches the view or feature it belongs to
(sections are marked with `<!-- === ViewName === -->` comments).

```xml
<data name="ButtonSaveProfile" xml:space="preserve">
  <value>Save Profile</value>
</data>
<data name="TipButtonSaveProfile" xml:space="preserve">
  <value>Save the current settings to this profile</value>
</data>
```

Always include `xml:space="preserve"` — it prevents XML parsers from collapsing whitespace
inside the value.

### Step 2 — Regenerate `Strings.Designer.cs`

In **Visual Studio**: save `Strings.resx` — the `ResXFileCodeGenerator` runs automatically
and updates `Strings.Designer.cs`.

From the **command line** (no Visual Studio required):

```bash
dotnet tool install -g dotnet-resx  # one-time install if not present
# Alternatively: just add the properties manually (see below)
```

If you prefer to skip the generator entirely, you can add the property manually to
`Strings.Designer.cs` following the same pattern used for every other key:

```csharp
/// <summary>Save Profile</summary>
public static string ButtonSaveProfile => G("ButtonSaveProfile");

/// <summary>Save the current settings to this profile</summary>
public static string TipButtonSaveProfile => G("TipButtonSaveProfile");
```

### Step 3 — Use the string

See [Using strings in AXAML](#using-strings-in-axaml) and
[Using strings in C# code](#using-strings-in-c-code) below.

### Step 4 — Translate (optional)

If a translation file exists for a language (e.g., `Strings.zh-Hans.resx`), add the
same key with the translated value there too.

---

## Key naming conventions

Keys use a **prefix that describes the UI role** followed by a `PascalCase` description.
This keeps the file scannable and makes the intent of each string obvious at a glance.

| Prefix | UI role | Example |
|--------|---------|---------|
| `Button` | Button label | `ButtonSave`, `ButtonOpenProject` |
| `Label` | Static label next to a control | `LabelEditingProfile`, `LabelModel` |
| `Tip` | Tooltip text | `TipButtonSave`, `TipLabelModel` |
| `AutoName` | `AutomationProperties.Name` (screen readers) | `AutoNameButtonSave` |
| `AutoHelp` | `AutomationProperties.HelpText` | `AutoHelpSearchBox` |
| `Text` | Longer prose, descriptions, warning banners | `TextInstallBannerDesc` |
| `Heading` | Section or page heading | `HeadingProfiles` |
| `Tab` | Tab strip label | `TabBackup`, `TabSettings` |
| `Watermark` | Placeholder text in a text input | `WatermarkSearch` |
| `Badge` | Small badge / chip label | `BadgeLabelCli`, `BadgeLabelDesktop` |
| `Header` | Column or table header | `HeaderProfileName`, `HeaderCreated` |
| `Menu` | Context-menu or menu-bar item | `MenuOpenFileLocation` |
| `Dialog` | Dialog title or prompt | `DialogTitleNewProfile` |
| `Msg` | Error or informational message (may contain `{0}` placeholders) | `MsgReservedProfileName` |
| `Status` | Status-bar message | `StatusReady`, `StatusSaving` |
| `Progress` | In-progress indicator text | `ProgressPreparing` |
| `StringFormat` | A format string used with `string.Format()` | `StringFormatCliActive` (`"CLI: {0}"`) |
| `Section` | Collapsible section title | `SectionClaudeAnthropicNode` |
| `Checkbox` | CheckBox content label | `CheckboxShowDialog` |

**Tips:**
- For a button that also has a tooltip, create both `ButtonXxx` and `TipButtonXxx`.
- For a button that is referenced by a screen reader, also create `AutoNameButtonXxx`.
- For messages with runtime values, use `{0}`, `{1}`, etc. and call `string.Format()` in code
  rather than building the string in the resx value.
- Emoji are fine in button/label strings (e.g., `💾 Save`) but avoid them in tooltips and
  accessibility strings where they may be read aloud literally.

---

## Using strings in AXAML

### Namespace declaration

Every AXAML file that uses localized strings must declare the `loc` namespace.
Add it to the root element alongside the other `xmlns` declarations:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:loc="clr-namespace:ClaudeForge.Localization"
        ...>
```

For `UserControl` files the declaration is identical — just on `<UserControl ...>` instead.

### Binding pattern

Use `{x:Static loc:Strings.KeyName}` wherever a string value is needed:

```xml
<!-- Button text and tooltip -->
<Button Content="{x:Static loc:Strings.ButtonSave}"
        ToolTip.Tip="{x:Static loc:Strings.TipButtonSave}"
        AutomationProperties.Name="{x:Static loc:Strings.AutoNameButtonSave}" />

<!-- Static label -->
<TextBlock Text="{x:Static loc:Strings.LabelModel}" />

<!-- Watermark on a TextBox -->
<TextBox Watermark="{x:Static loc:Strings.WatermarkSearch}" />

<!-- CheckBox -->
<CheckBox Content="{x:Static loc:Strings.CheckboxShowDialog}"
          ToolTip.Tip="{x:Static loc:Strings.TipCheckboxShowDialog}" />
```

`{x:Static}` is a compile-time binding resolved by Avalonia's AXAML compiler — it is
not a runtime `Binding` and does not respond to `INotifyPropertyChanged`. This means
**language changes at runtime require a restart** (or a full view rebuild) to take effect.
This is intentional and consistent with how most desktop apps handle locale switching.

### Format strings in AXAML

Format strings (keys with `{0}` placeholders) cannot be used directly in AXAML because
`{x:Static}` does not invoke `string.Format`. Perform formatting in the view-model and
bind to the resulting property instead:

```csharp
// ViewModel
public string CliActiveBadgeText =>
    string.Format(Strings.StringFormatCliActive, ActiveProfileName);
```

```xml
<!-- AXAML -->
<TextBlock Text="{Binding CliActiveBadgeText}" />
```

---

## Using strings in C# code

Import the namespace at the top of the file:

```csharp
using ClaudeForge.Localization;
```

Then access properties directly:

```csharp
// Simple string
StatusMessage = Strings.StatusReady;

// Format string with one argument
StatusMessage = string.Format(Strings.StatusProfileCreatedFmt, profileName);

// Conditional string based on product
string label = product == AboutProduct.ClaudeCode
    ? Strings.LabelDocumentation
    : Strings.LabelSupport;

// In a dialog call
await _dialogService.ShowAlertAsync(
    Strings.DialogTitleProfileExists,
    string.Format(Strings.MsgProfileAlreadyExists, name));
```

All properties are `static` — no instance is needed.

---

## Adding a new language

### 1. Create the translation file

Copy `Strings.resx` to a new file named `Strings.<culture>.resx` in the same directory.
The culture name must be a valid BCP 47 tag:

```
Strings.de.resx        # German
Strings.fr.resx        # French
Strings.ja.resx        # Japanese
Strings.zh-Hans.resx   # Simplified Chinese
Strings.zh-Hant.resx   # Traditional Chinese
Strings.pt-BR.resx     # Brazilian Portuguese
```

### 2. Translate the values

In the new file, replace every `<value>` with the translated text. Keep the `name`
attributes identical to the English file — the resource manager matches by key name,
not by position.

```xml
<!-- Strings.de.resx -->
<data name="ButtonSave" xml:space="preserve">
  <value>💾 Speichern</value>
</data>
<data name="TipButtonSave" xml:space="preserve">
  <value>Alle geänderten Einstellungen speichern (Strg+S)</value>
</data>
```

Leave any key untranslated to inherit the English fallback for that string — you do not
need to translate every key before shipping a partial translation.

### 3. Register the language in the project file

Open `src/ClaudeForge/ClaudeForge.csproj` and add the culture code to
`SatelliteResourceLanguages`:

```xml
<SatelliteResourceLanguages>en;zh-Hans;de</SatelliteResourceLanguages>
```

This tells the .NET SDK to include the satellite assembly for that language in
self-contained / single-file builds. Without this entry the translation is compiled
but not bundled, and `dotnet publish` will silently omit it.

### 4. Build and verify

```bash
dotnet build
# A satellite assembly is placed next to the main output:
#   bin/Debug/net10.0/de/ClaudeForge.resources.dll
```

To test, force the culture in `Program.Main` temporarily:

```csharp
LocalizationService.ApplyCulture("de");
```

Remove that line before committing.

### 5. Semi.Avalonia control strings

Semi.Avalonia (the UI theme library) has its own locale bundles for built-in control
strings (e.g., DataGrid column-sort labels, calendar month names). If the theme does not
ship a bundle for your target culture, its controls will display in English. This is
independent of the application strings.

---

## Culture resolution at startup

`LocalizationService.ApplyCulture()` is called **twice** — see
`src/ClaudeForge/Program.cs` and `src/ClaudeForge/App.axaml.cs`:

```csharp
// Program.Main — before BuildAvaloniaApp()
LocalizationService.ApplyCulture();

// App.OnFrameworkInitializationCompleted — after framework init
LocalizationService.ApplyCulture();
```

The resolution order inside `ApplyCulture`:

1. **Explicit argument** — pass a culture name to override everything (useful for testing).
2. **Stored user preference** — reads from `~/.claude/cache/ClaudeForge-gui-state.json`
   (not yet implemented; reserved for a future in-app language selector).
3. **OS culture** — `CultureInfo.CurrentUICulture` (the default).
4. **`en-US` fallback** — if the resolved culture name is invalid or unrecognised.

The culture is applied to all four slots so that both the .NET `ResourceManager` and
Semi.Avalonia's lazy-loaded locale bundles see the same value regardless of which thread
reads them first:

```csharp
CultureInfo.DefaultThreadCurrentCulture   = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;
Thread.CurrentThread.CurrentCulture       = culture;
Thread.CurrentThread.CurrentUICulture     = culture;
```

**Why call it twice?** Semi.Avalonia reads `CultureInfo.CurrentUICulture` on first
control creation (lazy load), which can happen on a background thread during
`BuildAvaloniaApp()`. Calling `ApplyCulture` before and after the framework init ensures
both windows are covered.

---

## Regenerating Strings.Designer.cs

`Strings.Designer.cs` is auto-generated by Visual Studio's `ResXFileCodeGenerator`
and is committed to source control. The `dotnet build` CLI uses the committed file
as-is and does **not** regenerate it automatically.

**In Visual Studio:** Save `Strings.resx` — the generator runs automatically.

**From the command line:** Add properties manually following the pattern in the file:

```csharp
/// <summary>Your English string here</summary>
public static string YourKeyName => G("YourKeyName");
```

The `G(string key)` helper returns the localized string for `key`, or the key name itself
if no value is found (fail-open so missing translations never produce blank UI).

**Important:** Always commit `Strings.Designer.cs` together with `Strings.resx` in the
same commit. Out-of-sync files cause build errors.

---

## Trimming and single-file builds

The app publishes as a trimmed, single-file executable in Release mode. The .NET trimmer
can remove resources it believes are unreachable. To prevent satellite assemblies from
being trimmed:

- Ensure each language is listed in `<SatelliteResourceLanguages>` (see step 3 above).
- Do not call `ResourceManager.GetString` with a dynamically-constructed key name — the
  trimmer cannot see through dynamic string construction and may remove strings it thinks
  are unused. Always access strings through `Strings.KeyName` (the static property).
- Do not pass `ResourceManager` to code the trimmer cannot analyse (e.g., reflection-heavy
  serialisation helpers). All existing usages in the codebase are static-property accesses
  and are trimmer-safe.

using System.Xml.Linq;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Localization;

/// <summary>
/// Locks the localization-parity contract across <c>Strings.resx</c> and every
/// <c>Strings.&lt;culture&gt;.resx</c> sibling.  These tests are dynamic — they
/// enumerate locale files at test time, so additional locales (Part 3 of the
/// project-backup-plans.md: ru-RU, ko-KR, ja-JP, fr-FR, ar-EG, etc.) are
/// automatically covered the moment their files exist on disk.
///
/// <para>
/// Context: an audit on 2026-05-23 surfaced 23 keys that drifted across all
/// four existing non-English locales — added to <c>Strings.zh-CN.resx</c> as
/// TODO placeholders, never added to de/es/pt at all.  Users running the app
/// in German, Spanish, or Portuguese silently fell back to English for those
/// keys; Chinese users saw English text in 23 places.  This test class is the
/// enforcement mechanism that prevents the same drift from recurring.
/// </para>
/// </summary>
[TestClass]
public sealed class LocalizationParityTests
{
    /// <summary>
    /// Threshold for the "copy-paste regression" detector.  A locale where more
    /// than this fraction of values is byte-identical to English is almost
    /// certainly a lazy duplicate of the English file, not a real translation.
    /// Current actual ratios across the four locales sit in the 4–6% range
    /// (legitimate shared identifiers — product names, URLs, glyphs, Latin
    /// loanwords like "Name" / "Timeout" / "Backup").  25% gives ~4–6x
    /// headroom over the current state while still catching the full-copy case
    /// (would register at ~99%).
    /// </summary>
    private const double MaxIdenticalToEnglishRatio = 0.25;

    /// <summary>
    /// Contract #1 — every key in <c>Strings.resx</c> exists in every
    /// non-English locale file.  Future drift is caught here, not in a user
    /// report.
    /// </summary>
    [TestMethod]
    public void EveryEnglishKey_HasEntryInEveryLocale()
    {
        string localizationDir = FindLocalizationDirectory();
        IReadOnlySet<string> englishKeys = LoadKeys(Path.Combine(localizationDir, "Strings.resx"));
        Assert.IsTrue(englishKeys.Count > 0,
            "Sanity check: Strings.resx must contain at least one <data name='…'> key.");

        IReadOnlyList<string> localeFiles = Directory.GetFiles(localizationDir, "Strings.*.resx")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.IsTrue(localeFiles.Count > 0,
            "Sanity check: at least one Strings.<culture>.resx file must exist.");

        List<string> failures = new();
        foreach (string localeFile in localeFiles)
        {
            string cultureCode = ExtractCultureCode(localeFile);
            IReadOnlySet<string> localeKeys = LoadKeys(localeFile);

            List<string> missing = englishKeys.Except(localeKeys, StringComparer.Ordinal)
                                              .OrderBy(k => k, StringComparer.Ordinal)
                                              .ToList();
            if (missing.Count > 0)
            {
                failures.Add(
                    $"[{cultureCode}] missing {missing.Count} key(s): " +
                    string.Join(", ", missing.Take(10)) +
                    (missing.Count > 10 ? $", … (+{missing.Count - 10} more)" : ""));
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail(
                "Localization drift detected — every English key must have a translation in " +
                "every non-English locale.  Missing entries:\n  " +
                string.Join("\n  ", failures));
        }
    }

    /// <summary>
    /// Contract #2 — no locale file may contain a "TODO" marker in either a
    /// <c>&lt;value&gt;</c> or <c>&lt;comment&gt;</c>.  The historical drift
    /// pattern was to add new keys to zh-CN with
    /// <c>&lt;comment&gt;TODO zh-CN translation&lt;/comment&gt;</c> as a punt;
    /// the placeholders accumulated, the keys never landed in de/es/pt, and
    /// the drift compounded.  This test prevents the same pattern from
    /// recurring.
    /// </summary>
    [TestMethod]
    public void NoLocaleFile_ContainsTodoMarker()
    {
        string localizationDir = FindLocalizationDirectory();
        IReadOnlyList<string> localeFiles = Directory.GetFiles(localizationDir, "Strings.*.resx")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.IsTrue(localeFiles.Count > 0,
            "Sanity check: at least one Strings.<culture>.resx file must exist.");

        List<string> failures = new();
        foreach (string localeFile in localeFiles)
        {
            XDocument doc = XDocument.Load(localeFile);
            foreach (XElement data in doc.Descendants("data"))
            {
                string keyName = data.Attribute("name")?.Value ?? "<unknown>";
                string? value = data.Element("value")?.Value;
                string? comment = data.Element("comment")?.Value;

                // Case-SENSITIVE match for the literal "TODO" marker.  The
                // legacy placeholder pattern was always all-caps (e.g.
                // "TODO zh-CN translation").  Going case-sensitive avoids
                // false positives from legitimate translated words that
                // contain the substring case-insensitively — Portuguese
                // "todos" / "Todos" (= "all"), Spanish "todos", the
                // mixed-case "TodoWrite" Claude-Code tool name, etc.
                if (value is not null && value.Contains("TODO", StringComparison.Ordinal))
                {
                    failures.Add($"[{ExtractCultureCode(localeFile)}] key '{keyName}' has TODO marker in <value>: {value}");
                }
                if (comment is not null && comment.Contains("TODO", StringComparison.Ordinal))
                {
                    failures.Add($"[{ExtractCultureCode(localeFile)}] key '{keyName}' has TODO marker in <comment>: {comment}");
                }
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail(
                "TODO placeholders detected — every locale entry must have a real " +
                "translation at the time it's introduced (no placeholder punts).\n  " +
                string.Join("\n  ", failures));
        }
    }

    /// <summary>
    /// Contract #3 — copy-paste detector.  For each non-English locale, the
    /// fraction of values byte-identical to English must stay under
    /// <see cref="MaxIdenticalToEnglishRatio"/>.  Catches the "lazy duplicate
    /// of Strings.resx" anti-pattern.  Legitimately-shared identifiers
    /// (product names, glyphs, URLs, Latin loanwords) do count toward the
    /// ratio but are far below the threshold in any real translation.
    /// </summary>
    [TestMethod]
    public void EachLocale_HasReasonableTranslationCoverage()
    {
        string localizationDir = FindLocalizationDirectory();
        IReadOnlyDictionary<string, string> englishValues = LoadValues(Path.Combine(localizationDir, "Strings.resx"));
        Assert.IsTrue(englishValues.Count > 0,
            "Sanity check: Strings.resx must contain at least one <data> with a <value>.");

        IReadOnlyList<string> localeFiles = Directory.GetFiles(localizationDir, "Strings.*.resx")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<string> failures = new();
        foreach (string localeFile in localeFiles)
        {
            string cultureCode = ExtractCultureCode(localeFile);
            IReadOnlyDictionary<string, string> localeValues = LoadValues(localeFile);

            int sharedKeyCount = 0;
            int identicalValueCount = 0;
            foreach (KeyValuePair<string, string> kv in englishValues)
            {
                if (!localeValues.TryGetValue(kv.Key, out string? localeValue))
                {
                    continue;
                }
                sharedKeyCount++;
                if (string.Equals(kv.Value, localeValue, StringComparison.Ordinal))
                {
                    identicalValueCount++;
                }
            }

            double ratio = sharedKeyCount == 0
                ? 0.0
                : (double)identicalValueCount / sharedKeyCount;

            if (ratio > MaxIdenticalToEnglishRatio)
            {
                failures.Add(
                    string.Format(CultureInfo.InvariantCulture,
                        "[{0}] {1}/{2} ({3:P1}) values byte-identical to English — exceeds " +
                        "{4:P0} threshold.  Likely indicates a copy-paste of Strings.resx " +
                        "rather than a real translation.",
                        cultureCode, identicalValueCount, sharedKeyCount, ratio, MaxIdenticalToEnglishRatio));
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail(
                "Copy-paste regression detected — at least one locale's values are " +
                "predominantly identical to English:\n  " +
                string.Join("\n  ", failures));
        }
    }

    /// <summary>
    /// Contract #4 — format-placeholder parity.  For every English value that
    /// contains <c>{N}</c> indexed placeholders, each locale that defines the same
    /// key must use the IDENTICAL set of placeholder indices.  <c>string.Format</c>
    /// resolves the value for the user's <c>CurrentUICulture</c>, so a locale that
    /// drops or renumbers a placeholder (e.g. the coercion strings
    /// <c>LabelEffortCoercedFmt</c> / <c>LabelEffortUnsupportedFmt</c> /
    /// <c>LabelModelEffortSummaryFmt</c>) throws <see cref="FormatException"/> at
    /// runtime for that locale instead of merely falling back to English — the one
    /// drift class that crashes rather than degrades, so the other three contracts
    /// don't cover it.
    /// </summary>
    [TestMethod]
    public void EveryFormatPlaceholder_MatchesAcrossLocales()
    {
        string localizationDir = FindLocalizationDirectory();
        IReadOnlyDictionary<string, string> englishValues = LoadValues(Path.Combine(localizationDir, "Strings.resx"));
        Assert.IsTrue(englishValues.Count > 0,
            "Sanity check: Strings.resx must contain at least one <data> with a <value>.");

        IReadOnlyList<string> localeFiles = Directory.GetFiles(localizationDir, "Strings.*.resx")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.IsTrue(localeFiles.Count > 0,
            "Sanity check: at least one Strings.<culture>.resx file must exist.");

        List<string> failures = new();
        foreach (string localeFile in localeFiles)
        {
            string cultureCode = ExtractCultureCode(localeFile);
            IReadOnlyDictionary<string, string> localeValues = LoadValues(localeFile);

            foreach (KeyValuePair<string, string> kv in englishValues)
            {
                IReadOnlySet<int> englishIndices = PlaceholderIndices(kv.Value);
                if (englishIndices.Count == 0)
                {
                    continue; // nothing to format → nothing to drift
                }

                // Key-presence is Contract #1's job; only compare where the locale
                // actually defines the key (a missing key falls back to English and
                // formats safely).
                if (!localeValues.TryGetValue(kv.Key, out string? localeValue))
                {
                    continue;
                }

                IReadOnlySet<int> localeIndices = PlaceholderIndices(localeValue);
                if (!englishIndices.SetEquals(localeIndices))
                {
                    failures.Add(string.Format(CultureInfo.InvariantCulture,
                        "[{0}] key '{1}': English uses placeholders {{{2}}} but the locale uses {{{3}}}.",
                        cultureCode, kv.Key,
                        string.Join(",", englishIndices.OrderBy(i => i)),
                        string.Join(",", localeIndices.OrderBy(i => i))));
                }
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail(
                "Format-placeholder drift detected — a localized format string must use the " +
                "exact same {N} placeholder set as its English source, or string.Format throws " +
                "FormatException at runtime for that locale:\n  " +
                string.Join("\n  ", failures));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The distinct numeric placeholder indices in a composite format string
    /// (<c>"{0} of {1}"</c> → <c>{0, 1}</c>; <c>"{0:P1}"</c> → <c>{0}</c>).  Escaped
    /// braces (<c>{{</c> / <c>}}</c>) are stripped first so a literal brace never
    /// registers as a placeholder.
    /// </summary>
    private static IReadOnlySet<int> PlaceholderIndices(string value)
    {
        string unescaped = value
            .Replace("{{", string.Empty, StringComparison.Ordinal)
            .Replace("}}", string.Empty, StringComparison.Ordinal);

        // Limitation: a format specifier containing literal braces (e.g. "{0:{nested}}")
        // is not parsed correctly — vanishingly rare in UI strings, and absent from this
        // resx. Revisit the pattern if a complex specifier is ever introduced.
        HashSet<int> indices = new();
        foreach (Match m in Regex.Matches(unescaped, @"\{(\d+)(?:[,:][^{}]*)?\}"))
        {
            indices.Add(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        }

        return indices;
    }

    /// <summary>
    /// Reads the set of <c>&lt;data name="…"&gt;</c> keys from a resx file.
    /// </summary>
    private static IReadOnlySet<string> LoadKeys(string resxPath)
    {
        try
        {
            XDocument doc = XDocument.Load(resxPath);
            return doc.Descendants("data")
                      .Select(d => d.Attribute("name")?.Value)
                      .Where(n => !string.IsNullOrEmpty(n))
                      .Select(n => n!)
                      .ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException)
        {
            // Surface the underlying failure so a corrupt resx is diagnosable
            // rather than silently looking like a zero-key file.
            throw new InvalidOperationException(
                $"Could not parse '{resxPath}' as XML — locale resx is unreadable.", ex);
        }
    }

    /// <summary>
    /// Reads the full <c>name → value</c> mapping from a resx file.  Missing
    /// <c>&lt;value&gt;</c> elements (rare) are skipped.
    /// </summary>
    private static IReadOnlyDictionary<string, string> LoadValues(string resxPath)
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal);
        XDocument doc = XDocument.Load(resxPath);
        foreach (XElement data in doc.Descendants("data"))
        {
            string? name = data.Attribute("name")?.Value;
            string? value = data.Element("value")?.Value;
            if (!string.IsNullOrEmpty(name) && value is not null)
            {
                map[name] = value;
            }
        }
        return map;
    }

    /// <summary>
    /// Extracts the culture code from a path like
    /// <c>…/Strings.de-DE.resx</c> → <c>"de-DE"</c>.
    /// </summary>
    private static string ExtractCultureCode(string localeFilePath)
    {
        // Strings.<culture>.resx  →  middle segment.
        string fileName = Path.GetFileNameWithoutExtension(localeFilePath); // "Strings.de-DE"
        int dot = fileName.IndexOf('.');
        return dot >= 0 && dot < fileName.Length - 1
            ? fileName[(dot + 1)..]
            : fileName;
    }

    /// <summary>
    /// Walks up from the test's runtime base directory to the repo root, then
    /// down to <c>src/ClaudeForge/Localization</c>.  The resx sources aren't
    /// bundled in the test assembly — the test reads them straight from the
    /// source tree.  Same pattern used by
    /// <see cref="Bennewitz.Ninja.ClaudeForge.Tests.Accessibility.AxamlAccessibilityCoverageTests"/>.
    /// </summary>
    private static string FindLocalizationDirectory()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            string candidate = Path.Combine(dir, "src", "ClaudeForge", "Localization");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            "Could not locate src/ClaudeForge/Localization/ by walking up from " +
            $"AppContext.BaseDirectory = '{AppContext.BaseDirectory}'.");
    }
}

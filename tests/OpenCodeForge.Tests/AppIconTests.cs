using System.Reflection;
using System.Xml.Linq;

namespace Bennewitz.Ninja.OpenCodeForge.Tests;

/// <summary>
/// The app icon's two shipped forms — the embedded PNG the window loads, and the
/// <c>.ico</c> the Windows apphost embeds — exist and are what they claim to be.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>These guard a replacement, not the current artwork.</b> What ships today is a
/// placeholder, and replacing it means regenerating three files by hand from one drawing:
/// the <c>.svg</c> (the Linux icon theme resolves <c>Icon=opencodeforge</c> against it), the
/// <c>.png</c> (loaded into <see cref="Avalonia.Controls.WindowIcon"/>), and the multi-resolution
/// <c>.ico</c>. Nothing rebuilds the raster forms from the vector, so the likely mistake is
/// replacing the SVG and leaving the other two — which shows up as an app whose launcher icon
/// changed and whose taskbar icon did not.
/// </para>
/// <para>
/// ⓘ These stop at file identity on purpose. Whether the bitmap <i>decodes</i> is Avalonia's
/// concern and needs a platform render surface this project deliberately does not stand up —
/// the shared headless harness is stripped of application resource dictionaries, which is why
/// every test here is view-model or file level.
/// </para>
/// </remarks>
[TestClass]
public sealed class AppIconTests
{
    /// <summary>The sizes <c>OpenCodeForge.ico</c> is expected to carry.</summary>
    /// <remarks>
    /// Windows picks per context — 16 for the titlebar, 32 for Alt-Tab, 256 for large icon
    /// views — and silently upscales the nearest it finds. An .ico missing its small frames
    /// therefore looks fine at 256 and blurry everywhere a user actually sees it.
    /// </remarks>
    private static readonly int[] ExpectedIcoSizes = [16, 32, 48, 64, 128, 256];

    private static readonly byte[] PngMagic = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Matches <c>BuildFilePathIntegrityTests.FindRepoRoot()</c>.</summary>
    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src")) && Directory.Exists(Path.Combine(dir, "tests")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            $"Could not locate the repo root by walking up from '{AppContext.BaseDirectory}'.");
    }

    private static string ResourcePath(string fileName) =>
        Path.Combine(FindRepoRoot(), "src", "OpenCodeForge", "Resources", fileName);

    /// <summary>
    /// The window icon is embedded in the built assembly, and the file behind it is a PNG.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The failure this catches is dropping the <c>&lt;AvaloniaResource&gt;</c> entry from the
    /// csproj — a one-line deletion that still compiles, still runs, and leaves every window
    /// with Avalonia's stock icon. <c>AppIcon</c> logs a warning and carries on by design, so
    /// nothing else in the app would fail.
    /// </para>
    /// <para>
    /// ⚠ <b>Read from the assembly's <c>!AvaloniaResources</c> blob rather than through
    /// <c>AssetLoader</c>.</b> <c>AssetLoader.Open</c> resolves <c>avares://</c> through
    /// <c>AvaloniaLocator</c> and throws <i>"Unable to locate 'Avalonia.Platform.IAssetLoader'"</i>
    /// without an initialised Avalonia runtime, which this project deliberately does not stand
    /// up. The manifest resource is the same evidence — it is what the build actually embedded —
    /// and needs no runtime at all.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void WindowIconIsEmbeddedInTheAssemblyAndTheSourceFileIsAPng()
    {
        Assembly app = typeof(AppIcon).Assembly;

        string? blobName = app
            .GetManifestResourceNames()
            .FirstOrDefault(n => n.Contains("AvaloniaResources", StringComparison.Ordinal));

        Assert.IsNotNull(
            blobName,
            $"'{app.GetName().Name}' embeds no Avalonia resource blob at all, so no "
            + "<AvaloniaResource> survived the build.");

        using Stream blob = app.GetManifestResourceStream(blobName)!;
        using MemoryStream copy = new();
        blob.CopyTo(copy);

        // The blob stores its entry paths as UTF-8 runs between binary length prefixes, so a
        // substring search over the decoded bytes is the cheap, format-version-independent way
        // to ask whether one path is present.
        string decoded = System.Text.Encoding.UTF8.GetString(copy.ToArray());

        Assert.IsTrue(
            decoded.Contains("/Resources/OpenCodeForge.png", StringComparison.Ordinal),
            "The built assembly's Avalonia resource blob does not contain "
            + "/Resources/OpenCodeForge.png, so avares://OpenCodeForge/Resources/OpenCodeForge.png "
            + "cannot resolve and every window falls back to Avalonia's stock icon. Check the "
            + "<AvaloniaResource> entry in OpenCodeForge.csproj.");

        // …and the file that entry points at is really a PNG.
        string pngPath = ResourcePath("OpenCodeForge.png");
        Assert.IsTrue(File.Exists(pngPath), $"'{pngPath}' is missing.");

        byte[] header = new byte[PngMagic.Length];
        using (FileStream file = File.OpenRead(pngPath))
        {
            int read = file.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            Assert.AreEqual(
                PngMagic.Length,
                read,
                $"'{pngPath}' holds fewer than {PngMagic.Length} bytes, so it is not an image.");
        }

        CollectionAssert.AreEqual(
            PngMagic,
            header,
            $"'{pngPath}' does not start with the PNG signature; WindowIcon would fail to "
            + "decode it.");
    }

    /// <summary>
    /// The <c>.ico</c> carries every size Windows selects between.
    /// </summary>
    [TestMethod]
    public void IcoCarriesEveryExpectedSize()
    {
        string icoPath = ResourcePath("OpenCodeForge.ico");
        Assert.IsTrue(
            File.Exists(icoPath),
            $"'{icoPath}' is missing, so <ApplicationIcon> names a file that does not exist and "
            + "the Windows build cannot embed an icon.");

        byte[] bytes = File.ReadAllBytes(icoPath);

        // ICONDIR: reserved(2)=0, type(2)=1 for icon, count(2). Then `count` ICONDIRENTRYs of
        // 16 bytes each, whose first two bytes are width and height — with 0 meaning 256,
        // because the field is a single byte and 256 does not fit in it.
        Assert.IsTrue(bytes.Length > 6, $"'{icoPath}' is too short to be an .ico.");
        Assert.AreEqual(0, BitConverter.ToUInt16(bytes, 0), "ICONDIR.reserved must be 0.");
        Assert.AreEqual(1, BitConverter.ToUInt16(bytes, 2), "ICONDIR.type must be 1 (icon).");

        int count = BitConverter.ToUInt16(bytes, 4);
        Assert.IsTrue(count > 0, $"'{icoPath}' declares no icon entries.");
        Assert.IsTrue(
            bytes.Length >= 6 + (count * 16),
            $"'{icoPath}' declares {count} entries but is too short to hold their directory.");

        List<int> sizes = [];
        for (int i = 0; i < count; i++)
        {
            int offset = 6 + (i * 16);
            int width = bytes[offset] == 0 ? 256 : bytes[offset];
            sizes.Add(width);
        }

        List<int> missing = [.. ExpectedIcoSizes.Where(s => !sizes.Contains(s))];

        Assert.IsTrue(
            missing.Count == 0,
            $"'{icoPath}' is missing size(s) {string.Join(", ", missing)}; it carries "
            + $"{string.Join(", ", sizes.Order())}. Windows upscales the nearest frame it finds, "
            + "so a missing small size is blurry exactly where the icon is most often seen. "
            + "Regenerate the .ico from the artwork with every size.");
    }

    /// <summary>
    /// The csproj's <c>&lt;ApplicationIcon&gt;</c> names a file that exists.
    /// </summary>
    /// <remarks>
    /// MSBuild fails the build on a missing ApplicationIcon, so this is not about catching a
    /// broken build — it is about catching the icon being pointed somewhere unintended, and
    /// about keeping the three artwork files named in one place a reader can check.
    /// </remarks>
    [TestMethod]
    public void ApplicationIconNamesAFileThatExists()
    {
        string repoRoot = FindRepoRoot();
        string csprojPath = Path.Combine(repoRoot, "src", "OpenCodeForge", "OpenCodeForge.csproj");

        string? declared = XDocument
            .Load(csprojPath)
            .Descendants("ApplicationIcon")
            .Select(e => e.Value.Trim())
            .FirstOrDefault();

        Assert.IsFalse(
            string.IsNullOrWhiteSpace(declared),
            "OpenCodeForge.csproj declares no <ApplicationIcon>, so the Windows apphost ships "
            + "with the generic .NET icon.");

        string absolute = Path.Combine(
            repoRoot, "src", "OpenCodeForge", declared!.Replace('\\', Path.DirectorySeparatorChar));

        Assert.IsTrue(
            File.Exists(absolute),
            $"<ApplicationIcon>{declared}</ApplicationIcon> resolves to '{absolute}', which does "
            + "not exist.");
    }

    /// <summary>
    /// The SVG the Linux icon theme resolves against exists, and still declares itself a
    /// placeholder.
    /// </summary>
    /// <remarks>
    /// ⭐ The second half is the point. The placeholder marker is the only thing standing between
    /// "we know this artwork is temporary" and a stand-in that quietly becomes the product's
    /// identity because nobody remembered it was one. When the real icon lands, this assertion
    /// fails — deliberately — and whoever replaces the artwork deletes it in the same change.
    /// </remarks>
    [TestMethod]
    public void IconSvgExistsAndSaysItIsAPlaceholder()
    {
        string svgPath = ResourcePath("OpenCodeForge.svg");
        Assert.IsTrue(
            File.Exists(svgPath),
            $"'{svgPath}' is missing. Publish-Rid.ps1 stages it into the Linux archive as "
            + "opencodeforge.svg, which is what Icon=opencodeforge resolves against.");

        string svg = File.ReadAllText(svgPath);

        Assert.IsTrue(
            svg.Contains("<svg", StringComparison.Ordinal),
            $"'{svgPath}' does not contain an <svg> element.");

        Assert.IsTrue(
            svg.Contains("PLACEHOLDER", StringComparison.Ordinal),
            $"'{svgPath}' no longer marks itself a placeholder. If the real artwork has landed, "
            + "delete this assertion in the same change — and check that OpenCodeForge.png and "
            + "OpenCodeForge.ico were regenerated from it, because nothing does that "
            + "automatically.");
    }
}

using System.Text.Json.Nodes;
using Bennewitz.Ninja.AgentForge.Core.FileIO;
using Bennewitz.Ninja.AgentForge.Core.Settings;

namespace Bennewitz.Ninja.AgentForge.Core.Tests.FileIO;

/// <summary>
/// The atomic save's final rename survives another process briefly holding the target open,
/// and still fails at once on the failures that waiting cannot fix.
/// </summary>
/// <remarks>
/// ⚠ <b>The lock tests are Windows-only, because the fault is.</b> Windows refuses a replacing
/// rename while any handle on the target lacks <c>FILE_SHARE_DELETE</c>; POSIX renames over an
/// open file without complaint. The predicate tests run everywhere.
/// <para>
/// Canaried 2026-11-16: with <c>MoveIntoPlaceAsync</c> reduced to a bare <c>File.Move</c>,
/// <see cref="Save_LandsOnce_ABriefLockOnTheTargetIsReleased"/> failed with
/// <see cref="UnauthorizedAccessException"/>, the exact exception from the app log.
/// </para>
/// </remarks>
public sealed class ConfigFileLoaderSharingRetryTests : IDisposable
{
    private readonly string _sandbox;

    public ConfigFileLoaderSharingRetryTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), $"cfl-share-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sandbox);
    }

    public void Dispose()
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(_sandbox))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_sandbox, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            /* a scanner may still hold a handle; leave the folder for the OS to reclaim */
        }
    }

    private static SettingsDocument EditedDoc(string path)
    {
        JsonObject root = new() { ["model"] = "sonnet" };
        SettingsDocument doc = new(ConfigScope.User, path, root, isReadOnly: false);
        doc.Root["model"] = "opus";
        return doc;
    }

    /// <summary>A handle on <paramref name="path"/> that refuses delete-sharing, as a scanner's does.</summary>
    private static FileStream HoldWithoutDeleteSharing(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    [Fact]
    public async Task Save_LandsOnce_ABriefLockOnTheTargetIsReleased()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses a rename over an open file.");

        string path = Path.Combine(_sandbox, "settings.json");
        await File.WriteAllTextAsync(path, """{"model":"sonnet"}""", TestContext.Current.CancellationToken);

        FileStream held = HoldWithoutDeleteSharing(path);
        Task release = Task.Run(async () =>
        {
            await Task.Delay(120, TestContext.Current.CancellationToken);
            await held.DisposeAsync();
        }, TestContext.Current.CancellationToken);

        try
        {
            await ConfigFileLoader.SaveAsync(EditedDoc(path), headerComment: null, TestContext.Current.CancellationToken);
        }
        finally
        {
            await release;
        }

        string after = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.True(after.Contains("opus", StringComparison.Ordinal),
            "The save must land once the other handle closes. Without the retry the rename fails "
            + "with 'Access to the path is denied' and the user's edit never reaches disk.");
        Assert.Empty(Directory.EnumerateFiles(_sandbox, "settings.json.tmp-*"));
    }

    [Fact]
    public async Task Save_StillFails_AndCleansUp_WhenTheLockOutlastsTheRetries()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses a rename over an open file.");

        string path = Path.Combine(_sandbox, "settings.json");
        await File.WriteAllTextAsync(path, """{"model":"sonnet"}""", TestContext.Current.CancellationToken);

        Exception? caught;
        using (HoldWithoutDeleteSharing(path))
        {
            caught = await Record.ExceptionAsync(() =>
                ConfigFileLoader.SaveAsync(EditedDoc(path), headerComment: null, TestContext.Current.CancellationToken));
        }

        Assert.True(caught is UnauthorizedAccessException or IOException,
            $"A lock that never clears must surface as the original failure, not be swallowed; got {caught?.GetType().Name ?? "no exception"}.");
        Assert.Empty(Directory.EnumerateFiles(_sandbox, "settings.json.tmp-*"));
        OrdinalAssert.Contains("sonnet", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AccessDenied_OnAnOrdinaryWritableFile_IsRetried()
    {
        string path = Path.Combine(_sandbox, "plain.json");
        File.WriteAllText(path, "{}");

        Assert.True(ConfigFileLoader.IsTransientReplaceFailure(new UnauthorizedAccessException(), path));
    }

    [Fact]
    public void AccessDenied_OnAReadOnlyFile_IsNotRetried()
    {
        string path = Path.Combine(_sandbox, "ro.json");
        File.WriteAllText(path, "{}");
        File.SetAttributes(path, FileAttributes.ReadOnly);

        Assert.False(ConfigFileLoader.IsTransientReplaceFailure(new UnauthorizedAccessException(), path),
            "A read-only target does not become writable by waiting.");
    }

    [Fact]
    public void AccessDenied_WhenTheTargetIsADirectory_IsNotRetried()
    {
        string path = Path.Combine(_sandbox, "blocked.json");
        Directory.CreateDirectory(path);

        Assert.False(ConfigFileLoader.IsTransientReplaceFailure(new UnauthorizedAccessException(), path),
            "A directory at the target path does not go away by waiting.");
    }

    [Theory]
    [InlineData(unchecked((int)0x80070020), true)]  // ERROR_SHARING_VIOLATION
    [InlineData(unchecked((int)0x80070021), true)]  // ERROR_LOCK_VIOLATION
    [InlineData(unchecked((int)0x80070070), false)] // ERROR_DISK_FULL
    [InlineData(unchecked((int)0x80070003), false)] // ERROR_PATH_NOT_FOUND
    public void IOException_IsRetried_OnlyForSharingAndLockViolations(int hresult, bool expected)
    {
        Assert.Equal(expected,
            ConfigFileLoader.IsTransientReplaceFailure(new IOException("x", hresult), Path.Combine(_sandbox, "x.json")));
    }

    [Fact]
    public void OtherExceptions_AreNotRetried()
    {
        Assert.False(ConfigFileLoader.IsTransientReplaceFailure(new ArgumentException(), Path.Combine(_sandbox, "x.json")));
    }
}

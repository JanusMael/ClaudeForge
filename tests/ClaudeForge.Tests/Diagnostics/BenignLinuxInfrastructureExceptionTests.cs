using Tmds.DBus.Protocol;

namespace Bennewitz.Ninja.ClaudeForge.Tests.Diagnostics;

/// <summary>
/// The filter <c>Program</c> hands AppServices as <c>IsBenignException</c>, which decides whether a
/// failure is logged at Debug and shown nothing, or logged at Error and shown in the crash dialog.
/// </summary>
/// <remarks>
/// ⛔ The D-Bus half used to compare <c>GetType().FullName</c> with
/// <c>"Tmds.DBus.Protocol.DBusException"</c>, a type the Tmds.DBus.Protocol that Avalonia 12 ships
/// does not have, so it matched nothing. These tests throw the package's real exceptions, which is
/// what makes them able to fail: a name comparison passes a test built from the same string.
/// </remarks>
public sealed class BenignLinuxInfrastructureExceptionTests
{
    public static TheoryData<Exception> DBusExceptions => new()
    {
        new DBusConnectFailedException("no session bus"),
        new DBusConnectionClosedException("closed"),
        new DBusMessageException("bad message"),
        new DBusReadException("read failed"),
        new DBusOwnerChangedException(),
    };

    [Theory]
    [MemberData(nameof(DBusExceptions))]
    public void EveryDBusExceptionIsBenign_OnEveryPlatform(Exception ex)
    {
        Assert.True(Program.IsBenignLinuxInfrastructureException(ex), ex.GetType().FullName);
    }

    [Fact]
    public void AnOrdinaryExceptionIsNotBenign()
    {
        Assert.False(Program.IsBenignLinuxInfrastructureException(new InvalidOperationException("boom")));
    }

    [Fact]
    public void ACancellationIsNotBenign_SoOneEscapingOnTheUiThreadIsStillShown()
    {
        Assert.False(Program.IsBenignLinuxInfrastructureException(new OperationCanceledException()));
    }

    [Fact]
    public void ADBusErrorNameInTheMessage_IsBenignOnLinuxOnly()
    {
        InvalidOperationException ex = new("org.freedesktop.DBus.Error.ServiceUnknown: no portal");

        Assert.Equal(OperatingSystem.IsLinux(), Program.IsBenignLinuxInfrastructureException(ex));
    }
}

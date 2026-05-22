using System.Reflection;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Tests;

/// <summary>
/// Placeholder until the real test suite lands in later phases. Exists so the
/// test project produces a runnable assembly and `dotnet test` has something to
/// execute while the refactor is mid-flight.
/// </summary>
[TestClass]
public sealed class ScaffoldingSanityTests
{
    [TestMethod]
    public void AbstractionsAreReferenced()
    {
        // Sanity: the library's interfaces are visible from the test project.
        Assert.IsTrue(typeof(IEditorSchema).IsInterface);
        Assert.IsTrue(typeof(IEditorValue).IsInterface);
        Assert.IsTrue(typeof(IEditorScope).IsInterface);
        Assert.IsTrue(typeof(IEditorWorkspace).IsInterface);
    }

    [TestMethod]
    public void AvaloniaPackageIsReferenced()
    {
        Assembly avaloniaAsm = typeof(AssemblyMarker).Assembly;
        Assert.IsNotNull(avaloniaAsm);
    }
}
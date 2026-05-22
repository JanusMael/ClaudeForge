using System.Collections;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

/// <summary>
/// Abstraction over <see cref="System.Environment"/> for testability.
/// </summary>
public interface IEnvironmentProvider
{
    /// <summary>Returns all environment variables for the given target.</summary>
    IDictionary GetVariables(EnvironmentVariableTarget target);

    /// <summary>
    /// Sets (or, when <paramref name="value"/> is null, deletes) an environment variable
    /// in the given target store.
    /// </summary>
    void SetVariable(string name, string? value, EnvironmentVariableTarget target);
}
using System.Collections;

namespace Bennewitz.Ninja.LayeredEditors.Avalonia.Services;

/// <summary>
/// Default <see cref="IEnvironmentProvider"/> that delegates directly to <see cref="System.Environment"/>.
/// </summary>
public sealed class DefaultEnvironmentProvider : IEnvironmentProvider
{
    public IDictionary GetVariables(EnvironmentVariableTarget target)
    {
        return Environment.GetEnvironmentVariables(target);
    }

    public void SetVariable(string name, string? value, EnvironmentVariableTarget target)
    {
        Environment.SetEnvironmentVariable(name, value, target);
    }
}
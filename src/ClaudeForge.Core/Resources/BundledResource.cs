using System.Reflection;

namespace Bennewitz.Ninja.ClaudeForge.Core;

/// <summary>
/// Reads files embedded under <c>src/ClaudeForge.Core/Assets/&lt;subNamespace&gt;/</c>
/// from the assembly manifest. Shared by <see cref="Schema.SchemaRegistry"/> (the
/// bundled JSON schemas) and the model-catalog loader so both resolve resource
/// names the same way.
/// </summary>
internal static class BundledResource
{
    /// <summary>
    /// Returns the bytes of the embedded resource at
    /// <c>Assets/&lt;subNamespace&gt;/&lt;fileName&gt;</c>, or <c>null</c> when no such
    /// resource exists.
    /// </summary>
    public static byte[]? TryRead(string subNamespace, string fileName)
    {
        Assembly assembly = typeof(BundledResource).Assembly;
        string resourceName = $"{ResourceHelper.ResourcePrefix}.Core.Assets.{subNamespace}.{fileName}";
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return null;
        }

        using MemoryStream ms = new();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}

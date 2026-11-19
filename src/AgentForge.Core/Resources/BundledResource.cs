using System.Reflection;

namespace Bennewitz.Ninja.AgentForge.Core;

/// <summary>
/// Reads files embedded under this assembly's <c>Assets/&lt;subNamespace&gt;/</c> from the
/// manifest: OpenCode's schemas, until plans/00008 step 7. Claude's schemas and model catalog
/// are embedded in <c>ClaudeForge.Sdk.Claude</c> since step 6b-3.
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
        string resourceName = ResourceHelper.AssetName(subNamespace, fileName);
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

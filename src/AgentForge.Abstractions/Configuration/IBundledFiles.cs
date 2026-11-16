namespace Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

/// <summary>
/// The files a product ships inside its own binary: its bundled schema, that schema's overlay,
/// and the descriptions of its enum values. Read by file name.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>This is how the neutral engines reach a product's data without knowing the product.</b>
/// The schema registry and the backup engine used to read these files from their own assembly's
/// manifest, which meant every product's schemas had to be embedded in the neutral library. Now
/// the product hands its files over through <see cref="ProductDescriptor.BundledFiles"/>, and the
/// data lives in the product's assembly (plans/00008, decision 4).
/// </para>
/// <para>
/// The file names are the engines' conventions, not the product's: the schema is
/// <see cref="ProductDescriptor.SchemaFileName"/>, its overlay is that name with <c>.overlay</c>
/// before the extension, and its enum descriptions the same with <c>.enumdescriptions</c>. A
/// product implements a lookup, never a naming scheme.
/// </para>
/// </remarks>
public interface IBundledFiles
{
    /// <summary>
    /// The bytes of the bundled file called <paramref name="fileName"/>, or
    /// <see langword="null"/> when this product ships no such file.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> is an ordinary answer: most schemas have no overlay, and most have no
    /// enum descriptions. Throw only for a file that exists and cannot be read.
    /// </remarks>
    byte[]? TryRead(string fileName);
}

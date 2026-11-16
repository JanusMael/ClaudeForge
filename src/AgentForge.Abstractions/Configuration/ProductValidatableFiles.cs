namespace Bennewitz.Ninja.AgentForge.Abstractions.Configuration;

/// <summary>
/// Config files inside a backup archive that a restore validates against this product's schema:
/// where they sit below the product's archive folder, what they are called, and whether to look in
/// subfolders too.
/// </summary>
/// <remarks>
/// <para>
/// <b>This replaces a table in the restore engine that named Claude's two descriptors.</b> The table
/// was already data, one row per group of files, but it could only hold products the neutral
/// library could name. On the descriptor, each product states its own rows (plans/00008).
/// </para>
/// <para>
/// ⚠ <b>A file that matches no row is restored without validation</b>, not refused. Validation is
/// a warning surface, so a missed file costs a warning the user never sees and nothing else, which
/// is exactly why a missing row is easy to miss. List every config file the product's sections
/// write.
/// </para>
/// </remarks>
/// <param name="SubPath">
/// Folders below the product's <see cref="ProductDescriptor.ArchiveFolder"/>, outermost first;
/// empty for the archive folder itself.
/// </param>
/// <param name="FileNames">
/// File names or <see cref="Directory.EnumerateFiles(string, string, SearchOption)"/> patterns,
/// such as <c>settings.json</c> or <c>*.json</c>.
/// </param>
/// <param name="Recursive">
/// <see langword="true"/> to match in every folder below <paramref name="SubPath"/>.
/// <para>
/// ⚠ Keep a file at the top of the archive folder non-recursive: a file of the same name captured
/// deeper, inside a project's data, is a different file and does not follow this schema.
/// </para>
/// </param>
public sealed record ProductValidatableFiles(
    IReadOnlyList<string> SubPath,
    IReadOnlyList<string> FileNames,
    bool Recursive);

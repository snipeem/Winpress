namespace Winpress.Models;

/// <summary>
/// One segment in the breadcrumb navigation bar shown above the file list.
/// The <see cref="Path"/> is the internal archive path for that level;
/// an empty string represents the archive root.
/// </summary>
public record BreadcrumbItem(string Name, string Path)
{
    /// <summary>True when this is the root (archive name) crumb.</summary>
    public bool IsRoot => string.IsNullOrEmpty(Path);
}

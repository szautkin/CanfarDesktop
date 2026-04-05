namespace CanfarDesktop.Models;

/// <summary>
/// One segment in the file browser breadcrumb bar.
/// </summary>
/// <param name="Label">Display name (e.g. "Documents").</param>
/// <param name="FullPath">Absolute path this segment represents.</param>
public record BreadcrumbSegment(string Label, string FullPath);

namespace CanfarDesktop.Models.Fits;

/// <summary>
/// One 80-character FITS header card: KEYWORD = VALUE / COMMENT.
/// </summary>
public readonly record struct FitsCard(string Keyword, string Value, string Comment);

using System.Text.Json.Serialization;

namespace CanfarDesktop.Models.Cutouts;

/// <summary>
/// Who cuts a cutout. The same region of the same file can be cut either way, and the two are not the
/// same product: CADC sends back only the part, while a local cut needs the whole file on this computer
/// first — and once it is there, is instant, offline and repeatable.
///
/// <para>Written by name, so a saved record reads the same whatever order these are declared in.</para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CutoutMethod>))]
public enum CutoutMethod
{
    /// <summary>Cut on CADC's side by its SODA service; only the part is downloaded.</summary>
    Soda,

    /// <summary>Cut on this computer, from the complete file already downloaded.</summary>
    Local,
}

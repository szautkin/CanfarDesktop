using System.Reflection;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Services.ImageDiscovery;

/// <summary>
/// Supplies the probe / inspector script bodies (loaded from embedded resources) and derives their
/// content-hashed upload filenames (<c>probe-&lt;hash&gt;.sh</c>) so editing a script auto-busts the
/// previously-uploaded copy in VOSpace.
/// </summary>
public class EmbeddedProbeScripts : IProbeScriptProvider
{
    public string HomeSubdirectory => ".verbinal";
    public string ProbeBody { get; }
    public string InspectorBody { get; }

    public string ProbeUploadFileName => $"probe-{Sha256.ShortHexOf(ProbeBody)}.sh";
    public string InspectorUploadFileName => $"inspector-{Sha256.ShortHexOf(InspectorBody)}.sh";

    public EmbeddedProbeScripts(string probeBody, string inspectorBody)
    {
        ProbeBody = probeBody;
        InspectorBody = inspectorBody;
    }

    /// <summary>Load the scripts from the given assembly's embedded resources (…probe.sh / …inspector.sh).</summary>
    public static EmbeddedProbeScripts FromAssembly(Assembly assembly)
        => new(ReadResource(assembly, "probe.sh"), ReadResource(assembly, "inspector.sh"));

    private static string ReadResource(Assembly assembly, string endsWith)
    {
        var name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(endsWith, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded resource ending in '{endsWith}' not found in {assembly.GetName().Name}.");
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

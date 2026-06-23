using System.Text.Json.Serialization;

namespace CanfarDesktop.Models;

public class SessionLaunchParams
{
    public string Type { get; set; } = "notebook";
    public string Name { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public int Cores { get; set; } = 2;
    public int Ram { get; set; } = 8;
    public int Gpus { get; set; }
    public string? Cmd { get; set; }

    // Headless launch (canonical opencadc/canfar client shape).
    /// <summary>Single whitespace-separated string the server reads via getParameter("args").</summary>
    public string? Args { get; set; }
    /// <summary>Ordered env vars; each becomes a repeated <c>env=KEY=VAL</c> form field.</summary>
    public List<KeyValuePair<string, string>> Env { get; set; } = [];
    /// <summary>Replica count (client-side loop; &lt; 1 is clamped to 1).</summary>
    public int Replicas { get; set; } = 1;

    public string? RegistryUsername { get; set; }
    public string? RegistrySecret { get; set; }
}

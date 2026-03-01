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
    public string? RegistryUsername { get; set; }
    public string? RegistrySecret { get; set; }
}

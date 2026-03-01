namespace CanfarDesktop.Models;

public class RecentLaunch
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string ImageLabel { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public string ResourceType { get; set; } = "flexible";
    public int Cores { get; set; }
    public int Ram { get; set; }
    public int Gpus { get; set; }
    public DateTime LaunchedAt { get; set; }
}

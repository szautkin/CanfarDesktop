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

    // Headless-only fields (null/1 for interactive entries) — a batch relaunch must resubmit the
    // same command line, not fall back to the container entrypoint.
    public string? Cmd { get; set; }
    public string? Args { get; set; }
    public int Replicas { get; set; } = 1;

    public DateTime LaunchedAt { get; set; }
}

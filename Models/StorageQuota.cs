using System.Text.Json.Serialization;

namespace CanfarDesktop.Models;

public class StorageQuota
{
    public long QuotaBytes { get; set; }
    public long UsedBytes { get; set; }

    public double QuotaGB => QuotaBytes / 1_073_741_824.0;
    public double UsedGB => UsedBytes / 1_073_741_824.0;
    public double UsagePercent => QuotaBytes > 0 ? (double)UsedBytes / QuotaBytes * 100 : 0;

    public string? LastModified { get; set; }
}

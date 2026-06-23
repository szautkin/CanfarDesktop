using CanfarDesktop.Models;

namespace CanfarDesktop.Helpers;

/// <summary>
/// Builds the POST form fields for a single replica of a headless Skaha job, matching the
/// canonical opencadc/canfar Python client wire shape: <c>type=headless</c>, <c>name</c>
/// (suffixed <c>-N</c> when more than one replica), <c>image</c>, optional <c>cmd</c>/<c>args</c>,
/// optional <c>cores</c>/<c>ram</c>/<c>gpus</c>, a repeated <c>env=KEY=VAL</c> per env var,
/// auto-injected <c>REPLICA_ID</c>/<c>REPLICA_COUNT</c>, and a <c>replicas</c> field only when
/// the count exceeds one.
/// </summary>
public static class HeadlessRequestBuilder
{
    public static List<KeyValuePair<string, string>> BuildFormPairs(SessionLaunchParams p, int replicaIndex, int replicaCount)
    {
        var count = Math.Max(1, replicaCount);
        var name = count == 1 ? p.Name : $"{p.Name}-{replicaIndex + 1}";

        var pairs = new List<KeyValuePair<string, string>>
        {
            new("type", "headless"),
            new("name", name),
            new("image", p.Image),
        };

        if (!string.IsNullOrEmpty(p.Cmd)) pairs.Add(new("cmd", p.Cmd));
        if (!string.IsNullOrEmpty(p.Args)) pairs.Add(new("args", p.Args));
        if (p.Cores > 0) pairs.Add(new("cores", p.Cores.ToString()));
        if (p.Ram > 0) pairs.Add(new("ram", p.Ram.ToString()));
        if (p.Gpus > 0) pairs.Add(new("gpus", p.Gpus.ToString()));

        foreach (var kv in p.Env)
            pairs.Add(new("env", $"{kv.Key}={kv.Value}"));

        // Python-client parity env vars, injected per replica.
        pairs.Add(new("env", $"REPLICA_ID={replicaIndex + 1}"));
        pairs.Add(new("env", $"REPLICA_COUNT={count}"));

        if (count > 1) pairs.Add(new("replicas", count.ToString()));

        return pairs;
    }
}

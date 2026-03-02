using System.Text.Json;
using System.Text.Json.Serialization;
using CanfarDesktop.Models;

namespace CanfarDesktop.Helpers;

// Source-generated JSON context for trimmed (Release) builds.
// In Debug, the standard reflection-based serializer is used via JsonOptions below.
// In Release (trimmed), this context provides the metadata the serializer needs.
[JsonSerializable(typeof(List<RecentLaunch>))]
[JsonSerializable(typeof(List<SkahaSessionResponse>))]
[JsonSerializable(typeof(SkahaSessionResponse))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(UserInfo))]
[JsonSerializable(typeof(SkahaStatsResponse))]
[JsonSerializable(typeof(CoreStats))]
[JsonSerializable(typeof(RamStats))]
[JsonSerializable(typeof(List<RawImage>))]
[JsonSerializable(typeof(SessionContext))]
[JsonSerializable(typeof(ResourceOptions))]
[JsonSerializable(typeof(GpuOptions))]
[JsonSerializable(typeof(List<string>))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
public partial class AppJsonContext : JsonSerializerContext;

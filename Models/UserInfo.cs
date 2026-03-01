using System.Text.Json.Serialization;

namespace CanfarDesktop.Models;

public class UserInfo
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("firstName")]
    public string? FirstName { get; set; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; set; }

    [JsonPropertyName("institute")]
    public string? Institute { get; set; }

    [JsonPropertyName("internalID")]
    public string? InternalId { get; set; }
}

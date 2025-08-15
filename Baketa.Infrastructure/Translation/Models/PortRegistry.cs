using System.Text.Json.Serialization;

namespace Baketa.Infrastructure.Translation.Models;

/// <summary>
/// ポートレジストリ情報
/// Issue #147 Phase 5: translation_ports.json構造
/// </summary>
public class PortRegistry
{
    [JsonPropertyName("active_ports")]
    public List<int> ActivePorts { get; set; } = [];
    
    [JsonPropertyName("servers")]
    public Dictionary<string, ServerInfo> Servers { get; set; } = [];
    
    [JsonPropertyName("last_updated")]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// サーバー情報
/// </summary>
public class ServerInfo
{
    [JsonPropertyName("language_pair")]
    public required string LanguagePair { get; set; }
    
    [JsonPropertyName("pid")]
    public required int Pid { get; set; }
    
    [JsonPropertyName("started_at")]
    public double StartedAt { get; set; }
    
    [JsonPropertyName("status")]
    public required string Status { get; set; }
    
    [JsonPropertyName("last_health_check")]
    public DateTime? LastHealthCheck { get; set; }
}

/// <summary>
/// サーバーステータス列挙型
/// </summary>
public enum ServerStatus
{
    Starting,
    Running,
    Stopping,
    Stopped,
    Error
}
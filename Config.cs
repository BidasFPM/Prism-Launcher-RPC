using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PrismRpc;

public class Config
{
    [JsonPropertyName("client_id")]         public string ClientId { get; set; } = "YOUR_CLIENT_ID";
    [JsonPropertyName("poll_seconds")]      public int PollSeconds { get; set; } = 5;
    [JsonPropertyName("prism_data_dir")]    public string PrismDataDir { get; set; } = "";
    [JsonPropertyName("strip_sort_prefix")] public bool StripSortPrefix { get; set; } = true;
    [JsonPropertyName("name_overrides")]    public Dictionary<string, string> NameOverrides { get; set; } = new();
    [JsonPropertyName("show")]              public ShowConfig Show { get; set; } = new();
    [JsonPropertyName("privacy")]           public PrivacyConfig Privacy { get; set; } = new();
    [JsonPropertyName("assets")]            public AssetsConfig Assets { get; set; } = new();
    [JsonPropertyName("buttons")]           public List<ButtonConfig> Buttons { get; set; } = new()
    {
        new ButtonConfig { Label = "Get Prism Launcher", Url = "https://prismlauncher.org" }
    };
}

public class ShowConfig
{
    [JsonPropertyName("launcher_idle")]     public bool LauncherIdle     { get; set; } = true;
    [JsonPropertyName("instance_name")]     public bool InstanceName     { get; set; } = true;
    [JsonPropertyName("minecraft_version")] public bool MinecraftVersion { get; set; } = true;
    [JsonPropertyName("mod_loader")]        public bool ModLoader        { get; set; } = true;
    [JsonPropertyName("mod_count")]         public bool ModCount         { get; set; } = true;
    [JsonPropertyName("server_address")]    public bool ServerAddress    { get; set; } = true;
    [JsonPropertyName("ram")]               public bool Ram              { get; set; } = true;
    [JsonPropertyName("java_version")]      public bool JavaVersion      { get; set; } = true;
    [JsonPropertyName("total_playtime")]    public bool TotalPlaytime    { get; set; } = true;
    [JsonPropertyName("session_timer")]     public bool SessionTimer     { get; set; } = true;
    [JsonPropertyName("buttons")]           public bool Buttons          { get; set; } = true;
}

public class PrivacyConfig
{
    [JsonPropertyName("hide_instance_name")] public bool HideInstanceName { get; set; }
    [JsonPropertyName("hide_ip_servers")]    public bool HideIpServers    { get; set; } = true;
}

public class AssetsConfig
{
    [JsonPropertyName("launcher")] public string Launcher { get; set; } = "prism";
    [JsonPropertyName("game")]     public string Game     { get; set; } = "minecraft";
    [JsonPropertyName("loaders")]  public Dictionary<string, string> Loaders { get; set; } = new()
    {
        ["Fabric"]   = "fabric",
        ["Quilt"]    = "quilt",
        ["Forge"]    = "forge",
        ["NeoForge"] = "neoforge",
        ["Vanilla"]  = "vanilla",
    };
}

public class ButtonConfig
{
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("url")]   public string Url   { get; set; } = "";
}

public static class ConfigLoader
{
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented               = true,
        PropertyNameCaseInsensitive = true,
        Encoder                     = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static Config Load(string path, out string? error)
    {
        error = null;
        var defaultNode = JsonSerializer.SerializeToNode(new Config(), JsonOpts)!.AsObject();

        if (!File.Exists(path))
        {
            try { File.WriteAllText(path, defaultNode.ToJsonString(JsonOpts)); }
            catch (Exception ex) { Log.Warn($"Cannot create config: {ex.Message}"); }
        }

        JsonObject userNode;
        string originalText;
        try
        {
            originalText = File.ReadAllText(path);
            if (originalText.Length > 0 && originalText[0] == '\uFEFF')
                originalText = originalText[1..];
            userNode = string.IsNullOrWhiteSpace(originalText)
                ? new JsonObject()
                : (JsonNode.Parse(originalText)?.AsObject() ?? new JsonObject());
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return new Config();
        }

        Migrate(userNode);
        var merged = DeepMerge(defaultNode, userNode);

        try
        {
            var mergedText = merged.ToJsonString(JsonOpts);
            if (mergedText != originalText)
                File.WriteAllText(path, mergedText);
        }
        catch (Exception ex) { Log.Warn($"Could not write merged config: {ex.Message}"); }

        try
        {
            return merged.Deserialize<Config>(JsonOpts) ?? new Config();
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return new Config();
        }
    }

    private static JsonNode DeepMerge(JsonNode baseNode, JsonNode overNode)
    {
        if (baseNode is JsonObject bo && overNode is JsonObject oo)
        {
            var result = new JsonObject();
            foreach (var kv in bo)
                result[kv.Key] = oo.TryGetPropertyValue(kv.Key, out var ov) && ov != null
                    ? DeepMerge(kv.Value!, ov)
                    : kv.Value?.DeepClone();
            foreach (var kv in oo)
                if (!bo.ContainsKey(kv.Key))
                    result[kv.Key] = kv.Value?.DeepClone();
            return result;
        }
        return overNode.DeepClone();
    }

    private static void Migrate(JsonObject root)
    {
        if (root["show"] is not JsonObject show) return;
        var priv = root["privacy"] as JsonObject;

        bool? legacyShow = show["server"] is JsonValue sv ? TryBool(sv) : null;
        bool? legacyHide = priv?["hide_server_address"] is JsonValue hv ? TryBool(hv) : null;

        show.Remove("server");
        priv?.Remove("hide_server_address");

        if (!show.ContainsKey("server_address") && (legacyHide == true || legacyShow == false))
            show["server_address"] = false;
    }

    private static bool? TryBool(JsonValue v)
    {
        try { if (v.TryGetValue<bool>(out var b)) return b; } catch { }
        return null;
    }
}

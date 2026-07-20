using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CentrED.IO.Models;

public class Profile
{
    private const string PROFILE_FILE = "profile.json";
    private const string LOCATIONS_FILE = "locations.json";
    private const string LAND_TILE_SETS_FILE = "landtilesets.json";
    private const string STATIC_TILE_SETS_FILE = "statictilesets.json";
    private const string HUE_SETS_FILE = "huesets.json";
    private const string LAND_BRUSH_FILE = "landbrush.json";
    private const string STATIC_FILTER_FILE = "staticfilter.json";

    [JsonIgnore] public string Name { get; set; } = "";
    public string Hostname { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 2597;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string ClientPath { get; set; } = "";
    [JsonIgnore]
    public Dictionary<string, RadarFavorite> RadarFavorites { get; set; } = new();
    [JsonIgnore] public Dictionary<string, List<ushort>> LandTileSets { get; set; } = new();
    [JsonIgnore] public Dictionary<string, List<ushort>> StaticTileSets { get; set; } = new();
    [JsonIgnore] public Dictionary<string, SortedSet<ushort>> HueSets { get; set; } = new();
    [JsonIgnore] public Dictionary<string, LandBrush> LandBrush { get; set; } = new();
    [JsonIgnore] public List<int> StaticFilter { get; set; } = new();


    public void Serialize(String path)
    {
        var profileDir = Path.Join(path, Name);
        if (!Directory.Exists(profileDir))
        {
            Directory.CreateDirectory(profileDir);
        }
        File.WriteAllText(Path.Join(profileDir, PROFILE_FILE), JsonSerializer.Serialize(this, CedJsonContext.Default.Profile));
        File.WriteAllText(Path.Join(profileDir, LOCATIONS_FILE), JsonSerializer.Serialize(RadarFavorites, CedJsonContext.Default.DictionaryStringRadarFavorite));
        File.WriteAllText(Path.Join(profileDir, LAND_TILE_SETS_FILE), JsonSerializer.Serialize(LandTileSets, CedJsonContext.Default.DictionaryStringListUInt16));
        File.WriteAllText(Path.Join(profileDir, STATIC_TILE_SETS_FILE), JsonSerializer.Serialize(StaticTileSets, CedJsonContext.Default.DictionaryStringListUInt16));
        File.WriteAllText(Path.Join(profileDir, HUE_SETS_FILE), JsonSerializer.Serialize(HueSets, CedJsonContext.Default.DictionaryStringSortedSetUInt16));
        File.WriteAllText(Path.Join(profileDir, LAND_BRUSH_FILE), JsonSerializer.Serialize(LandBrush, CedJsonContext.Default.DictionaryStringLandBrush));
        File.WriteAllText(Path.Join(profileDir, STATIC_FILTER_FILE), JsonSerializer.Serialize(StaticFilter, CedJsonContext.Default.ListInt32));
    }

    public static Profile? Deserialize(string profileDir)
    {
        DirectoryInfo dir = new DirectoryInfo(profileDir);
        if (!dir.Exists)
            return null;

        var profile = JsonSerializer.Deserialize(File.ReadAllText(Path.Join(profileDir, PROFILE_FILE)), CedJsonContext.Default.Profile);
        if (profile == null)
            return null;
        profile.Name = dir.Name;

        var favorites = Deserialize(Path.Join(profileDir, LOCATIONS_FILE), CedJsonContext.Default.DictionaryStringRadarFavorite);
        if (favorites != null)
            profile.RadarFavorites = favorites;

        var landTileSets = Deserialize(Path.Join(profileDir, LAND_TILE_SETS_FILE), CedJsonContext.Default.DictionaryStringListUInt16);
        if (landTileSets != null)
            profile.LandTileSets = landTileSets;

        var staticTileSets = Deserialize(Path.Join(profileDir, STATIC_TILE_SETS_FILE), CedJsonContext.Default.DictionaryStringListUInt16);
        if (staticTileSets != null)
            profile.StaticTileSets = staticTileSets;

        var huesets = Deserialize(Path.Join(profileDir, HUE_SETS_FILE), CedJsonContext.Default.DictionaryStringSortedSetUInt16);
        if (huesets != null)
            profile.HueSets = huesets;

        var landBrush = Deserialize(Path.Join(profileDir, LAND_BRUSH_FILE), CedJsonContext.Default.DictionaryStringLandBrush);
        if (landBrush != null)
            profile.LandBrush = landBrush;

        var staticFilter = Deserialize(Path.Join(profileDir, STATIC_FILTER_FILE), CedJsonContext.Default.ListInt32);
        if (staticFilter != null)
            profile.StaticFilter = staticFilter;

        return profile;
    }

    private static T? Deserialize<T>(string filePath, JsonTypeInfo<T> typeInfo)
    {
        if (!File.Exists(filePath))
            return default;
        return JsonSerializer.Deserialize(File.ReadAllText(filePath), typeInfo);
    }

    public void SerializeStaticFilter(string path)
    {
        var profileDir = Path.Join(path, Name);
        if (!Directory.Exists(profileDir))
        {
            Directory.CreateDirectory(profileDir);
        }
        
        File.WriteAllText(Path.Join(profileDir, STATIC_FILTER_FILE), JsonSerializer.Serialize(StaticFilter, CedJsonContext.Default.ListInt32));
    }

}
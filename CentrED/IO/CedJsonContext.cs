using System.Text.Json.Serialization;
using CentrED.IO.Models;

namespace CentrED.IO;

[JsonSourceGenerationOptions(IncludeFields = true, WriteIndented = true)]
[JsonSerializable(typeof(ConfigRoot))]
[JsonSerializable(typeof(Profile))]
[JsonSerializable(typeof(Dictionary<string, RadarFavorite>))]
[JsonSerializable(typeof(Dictionary<string, List<ushort>>))]
[JsonSerializable(typeof(Dictionary<string, SortedSet<ushort>>))]
[JsonSerializable(typeof(Dictionary<string, LandBrush>))]
[JsonSerializable(typeof(List<int>))]
public partial class CedJsonContext : JsonSerializerContext;

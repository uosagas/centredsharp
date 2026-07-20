using System.Xml.Linq;

namespace CentrED.IO.Models.Centredplus;

public class TilesBrush
{
    public List<Brush> Brush = new();

    public static TilesBrush Load(Stream stream)
    {
        var root = XDocument.Load(stream).Root ?? throw new InvalidDataException("Empty TilesBrush xml");
        var result = new TilesBrush();
        foreach (var brushElement in root.Elements("Brush"))
        {
            var brush = new Brush
            {
                Id = (string?)brushElement.Attribute("Id") ?? "",
                Name = (string?)brushElement.Attribute("Name") ?? "",
            };
            foreach (var landElement in brushElement.Elements("Land"))
            {
                brush.Land.Add
                (
                    new Land
                    {
                        ID = (string?)landElement.Attribute("ID") ?? "",
                        Chance = (string?)landElement.Attribute("Chance") ?? "",
                    }
                );
            }
            foreach (var edgeElement in brushElement.Elements("Edge"))
            {
                var edge = new Edge
                {
                    To = (string?)edgeElement.Attribute("To") ?? "",
                };
                foreach (var edgeLandElement in edgeElement.Elements("Land"))
                {
                    edge.Land.Add
                    (
                        new EdgeLand
                        {
                            Type = (string?)edgeLandElement.Attribute("Type") ?? "",
                            ID = (string?)edgeLandElement.Attribute("ID") ?? "",
                        }
                    );
                }
                brush.Edge.Add(edge);
            }
            result.Brush.Add(brush);
        }
        return result;
    }
}

public class Brush
{
    public String Id = "";
    public String Name = "";
    public List<Land> Land = new();
    public List<Edge> Edge = new();
}

public class Land
{
    public String ID = "";
    public String Chance = "";
}

public class Edge
{
    public String To = "";
    public List<EdgeLand> Land = new();
}

public class EdgeLand
{
    public String Type = "";
    public String ID = "";
}

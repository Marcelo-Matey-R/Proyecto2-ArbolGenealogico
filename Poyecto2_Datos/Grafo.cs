// See https://aka.ms/new-console-template for more information

using System.Globalization;
using Microsoft.VisualBasic;
using Google.OpenLocationCode;
using Mapsui.Projections;
class Persona
{
    public int id;
    public string name;
    public int age;
    public DateTime birthdate;
    public int parentId;
    public string urlImage;
    public string addresPlusCode;
    public double lon;
    public double lat;

    public Persona(int Id, string Name, int Age, DateTime BirthDate, string photo, string Addres, double Lon, double Lat)
    {
        this.id = Id;
        this.name = Name;
        this.age = Age;
        this.birthdate = BirthDate;
        this.urlImage = photo;
        this.addresPlusCode = Addres;
        this.lon = Lon;
        this.lat = Lat;
    }
}

class Node
{
    public Persona familiar;
    public string partner;
    public Node parent;
    public List<Node> children = new List<Node>();
    public Node(Persona fam, string part, Node par)
    {
        this.familiar = fam;
        this.partner = part;
        this.parent = par;
    }

}

class Edge
{
    public Persona fam1;
    public Persona fam2;

    public double weight;

    public Edge(Persona per1, Persona per2, double w)
    {
        this.fam1 = per1;
        this.fam2 = per2;

        this.weight = w;
    }

}

class CalcDistance
{
    public (double, double) ConvertedCoor(string plus)
    {
        CodeArea area = OpenLocationCode.Decode(plus);
        double lon = area.CenterLongitude;
        double lat = area.CenterLatitude;
        return (lon, lat);
    }

    public double Distance(double lon1, double lat1, double lon2, double lat2)
    {
        var (x1, y1) = Mapsui.Projections.SphericalMercator.FromLonLat(lon1, lat1);
        var (x2, y2) = Mapsui.Projections.SphericalMercator.FromLonLat(lon2, lat2);

        var dx = x1 - x2;
        var dy = y1 - y2;

        var dis = Math.Sqrt(dx * dx + dy * dy);

        return dis;
    }
}

static class Tree
{
    
}
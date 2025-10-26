// See https://aka.ms/new-console-template for more information

using System.Globalization;
using Microsoft.VisualBasic;


class Persona
{
    public int id;
    public string name;
    public int age;
    public DateTime birthdate;
    public string addresPlusCode;
    public double lon;
    public double lat;

    public Persona(int Id, string Name, int Age, DateTime BirthDate, string Addres, double Lon, double Lat)
    {
        this.id = Id;
        this.name = Name;
        this.age = Age;
        this.birthdate = BirthDate;
        this.addresPlusCode = Addres;
        this.lon = Lon;
        this.lat = Lat;
    }
}

class Node
{
    public Persona familiar;
    public string partner;
    public Persona father;
    public List<Node> children = new List<Node>();
    public Node(Persona fam, string part, Persona dad)
    {
        this.familiar = fam;
        this.partner = part;
        this.father = dad;
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



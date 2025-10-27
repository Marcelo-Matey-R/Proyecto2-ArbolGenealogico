using System.Globalization;
using Microsoft.VisualBasic;
using Google.OpenLocationCode;
using Mapsui.Projections;
using Mapsui.Extensions;

// Creamos algunas personas con coordenadas (lon, lat)
// Ejemplo sencillo (puntos cercanos)
var p1 = new Persona("A", "Ana", 30, DateTime.Parse("1995-01-01"), Lon: -84.0910, Lat: 9.9350); // San José aproximado
var p2 = new Persona("B", "Bruno", 40, DateTime.Parse("1985-01-01"), Lon: -84.0910, Lat: 9.9350);
var p3 = new Persona("C", "Carla", 25, DateTime.Parse("1999-01-01"), Lon: -84.0925, Lat: 9.9355);
var p4 = new Persona("D", "Diego", 50, DateTime.Parse("1975-01-01"), Lon: -84.0850, Lat: 9.9300);

// Asignamos relaciones (usamos GUIDs generados en cada Persona)
// Para la prueba, haremos: Ana es raíz, Bruno y Carla hijos de Ana, Diego hijo de Bruno
p2.parentId = p1.id;
p3.parentId = p1.id;
p4.parentId = p2.id;

var people = new[] { p1, p2, p3, p4 };

var tm = new TreeManager();
tm.BuildFromPersons(people, null);

// Mostrar aristas y matriz de distancias
tm.PrintEdges();
tm.PrintDistanceMatrix();

// Mostrar pareja con mayor y menor distancia
Console.WriteLine("\nPareja con máxima distancia:");
if (tm.personasMaxDistance.Item1 != null)
    Console.WriteLine($" {tm.personasMaxDistance.Item1.name} <-> {tm.personasMaxDistance.Item2?.name}");
else
    Console.WriteLine(" No hay pareja calculada.");

Console.WriteLine("\nPareja con mínima distancia:");
if (tm.personasMinDistance.Item1 != null)
    Console.WriteLine($" {tm.personasMinDistance.Item1.name} <-> {tm.personasMinDistance.Item2?.name}");
else
    Console.WriteLine(" No hay pareja calculada.");

foreach (var p in people)
{
    OpenLocationCode ar = new OpenLocationCode((double)p.lon, (double)p.lat);

    Console.WriteLine($"Locaciones de {p} en Plus Code es {ar}");
}

Console.WriteLine("\nFIN prueba. Presiona cualquier tecla para salir...");
Console.ReadKey();




class Persona
{
    public Guid id { get; private set; }
    public string ownId;
    public string name;
    public int age;
    public DateTime birthdate;
    public Guid? parentId;
    public string urlImage;
    public string addresPlusCode;
    public double? lon;
    public double? lat;

    public Persona(string id, string Name, int Age, DateTime BirthDate, string photo = "",
    string Addres = "", double? Lon = null, double? Lat = null, Guid? ParentId = null)
    {
        this.id = Guid.NewGuid();
        this.ownId = id;
        this.name = Name;
        this.age = Age;
        this.birthdate = BirthDate;
        this.urlImage = photo ?? "";
        this.addresPlusCode = Addres;
        this.lon = Lon;
        this.lat = Lat;
        this.parentId = ParentId;
    }
    public int CalcAge()
    {
        var today = DateTime.Today;
        int Age = today.Year - birthdate.Year;

        if (birthdate.Date > today.AddYears(-Age)) Age--;
        return Age;
    }

    public bool HasCoordinates() => lon.HasValue && lat.HasValue;

    public bool EnsureCoordinatesFromPlusCode(CalcDistance calc)
    {
        if (HasCoordinates()) return true;
        if (string.IsNullOrWhiteSpace(addresPlusCode)) return false;
        if (calc.TryConvertPlusCode(addresPlusCode, out double Lon, out double Lat))
        {
            lon = Lon;
            lat = Lat;
            return true;
        }
        return false;
    }

    public override string ToString() => $"{name} (id={ownId})";
}

class Node
{
    public Persona familiar { get; private set; }
    public string partner { get; set; }
    public Node? parent { get; private set; }
    public List<Node> children { get; } = new List<Node>();

    public Dictionary<Node, double> distances = new Dictionary<Node, double>();
    public List<Edge> edges = new List<Edge>();
    public Node(Persona fam, string part = "")
    {
        this.familiar = fam ?? throw new ArgumentNullException(nameof(fam));
        this.partner = part ?? "";
    }

    public void AddChild(Node child)
    {
        if (child == null) throw new ArgumentNullException(nameof(child));

        if (!children.Contains(child))
        {
            child.DetachFromParent();
            child.parent = this;
            children.Add(child);
        }
    }
    public void DetachFromParent()
    {
        if (parent != null)
        {
            parent.children.Remove(this);
            parent = null;
        }
    }
    public int GetLevel()
    {
        int lvl = 0;
        var curr = this.parent;

        while (curr != null)
        {
            lvl++;
            curr = curr.parent;
        }
        return lvl;
    }

    public void TransverseDFS(Action<Node> action)
    {
        action?.Invoke(this);
        foreach (var c in children)
        {
            c.TransverseDFS(action);
        }
    }

}

class Edge
{
    public Node fam1;
    public Node fam2;

    public double weight;

    public Edge(Node per1, Node per2, double w)
    {
        this.fam1 = per1;
        this.fam2 = per2;

        this.weight = w;
    }
    public override string ToString() => $"{fam1.familiar.name} -> {fam2.familiar.name}: {weight:F0}"; 

}

class CalcDistance
{
    public bool TryConvertPlusCode(string plus, out double Lon, out double Lat)
    {
        Lon = Lat = 0;
        if (string.IsNullOrWhiteSpace(plus)) return false;
        try
        {
            CodeArea area = OpenLocationCode.Decode(plus);
            Lon = area.CenterLongitude;
            Lat = area.CenterLatitude;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public double Distance(double? lon1, double? lat1, double? lon2, double? lat2)
    {
        if (!lon1.HasValue || !lon2.HasValue || !lat1.HasValue || !lat2.HasValue)
        {
            return double.NaN;
        }
        var (x1, y1) = SphericalMercator.FromLonLat((double)lon1, (double)lat1);
        var (x2, y2) = SphericalMercator.FromLonLat((double)lon2, (double)lat2);

        var dx = x1 - x2;
        var dy = y1 - y2;

        var dis = Math.Sqrt(dx * dx + dy * dy);

        return dis;
    }
}



class TreeManager
{
    private readonly Dictionary<Guid, Node> _lookup = new Dictionary<Guid, Node>();
    private readonly List<Node> _roots = new List<Node>();
    private CalcDistance calcDistance = new CalcDistance();

    public IReadOnlyList<Node> Roots => _roots.AsReadOnly();
    public (Persona?, Persona?) personasMaxDistance = (null, null);
    public (Persona?, Persona?) personasMinDistance  = (null, null);

    public Node? FindNodeById(Guid id) => _lookup.TryGetValue(id, out var n) ? n : null;

    public void BuildFromPersons(IEnumerable<Persona> people, Func<Persona, Guid?> parentSelector = null)
    {
        _lookup.Clear();
        _roots.Clear();

        // Crear nodo por persona
        foreach (var p in people)
            _lookup[p.id] = new Node(p);

        // Enlazar
        foreach (var p in people)
        {
            Guid id = p.id;
            Guid? parentId = parentSelector?.Invoke(p) ?? p.parentId;

            if (parentId == null)
            {
                _roots.Add(_lookup[id]);
            }
            else if (_lookup.ContainsKey(parentId.Value))
            {
                var parentNode = _lookup[parentId.Value];
                parentNode.AddChild(_lookup[id]);
            }
            else
            {
                _roots.Add(_lookup[id]);
            }
        }

        UpdateGraphAndDistances();
    }

    public Node AddPerson(Persona p, Guid? parentId = null)
    {
        if (p == null) throw new ArgumentNullException(nameof(p));
        if (_lookup.ContainsKey(p.id)) throw new InvalidOperationException($"Ya existe una persona con id={p.id}");

        var node = new Node(p);
        _lookup[p.id] = node;

        if (parentId == null)
        {
            _roots.Add(node);
        }
        else
        {
            if (!_lookup.TryGetValue(parentId.Value, out var parentNode))
            {
                _roots.Add(node);
            }
            else
            {
                if (IsAncestor(descendant: parentNode, ancestorCandidate: node))
                    throw new InvalidOperationException("Operación inválida: el nuevo padre sería descendiente del nodo (crearía un ciclo).");

                parentNode.AddChild(node);
            }
        }

        UpdateGraphAndDistances();

        return node;
    }

    public void GetEdgesWithWeights(CalcDistance calc)
    {
        foreach (var node in _lookup.Values) node.edges.Clear();

        foreach (var root in _roots)
        {
            root.TransverseDFS(n =>
            {
                foreach (var child in n.children)
                {
                    var a = n;
                    var b = child;
                    double w = calc.Distance(a.familiar.lon, a.familiar.lat, b.familiar.lon, b.familiar.lat); // devuelve NaN si falta coord
                    var e1 = new Edge(a, b, w);
                    var e2 = new Edge(b, a, w);
                    a.edges.Add(e1);
                    b.edges.Add(e2);
                }
            });
        }
    }

    private bool IsAncestor(Node descendant, Node ancestorCandidate)
    {
        var cur = descendant;
        while (cur != null)
        {
            if (cur == ancestorCandidate) return true;
            cur = cur.parent;
        }
        return false;
    }

    public Dictionary<Node,double> Dijkstra(Node source)
    {
        var dist = _lookup.Values.ToDictionary(x => x, x => double.PositiveInfinity);
        var visited = new HashSet<Node>();
        dist[source] = 0;

        var pq = new PriorityQueue<Node, double>();
        pq.Enqueue(source, 0.0);

        while (pq.Count > 0)
        {
            pq.TryDequeue(out var curNode, out var curDist);
            // Si ya visitado lo ignoramos
            if (visited.Contains(curNode)) continue;
            visited.Add(curNode);

            foreach (var e in curNode.edges)
            {
                if (double.IsNaN(e.weight)) continue;

                var nd = curDist + e.weight;
                if (nd < dist[e.fam2])
                {
                    dist[e.fam2] = nd;
                    pq.Enqueue(e.fam2, nd);
                }
            }
        }

        return dist;
    }

    public void ComputeAllDijkstras()
    {
        foreach (var n in _lookup.Values)
            n.distances = Dijkstra(n);
    }

    private void UpdateGraphAndDistances()
    {
        GetEdgesWithWeights(calcDistance);

        ComputeAllDijkstras();

        ComputeMinMaxPairs();
    }
    public void ComputeMinMaxPairs()
    {
        personasMaxDistance = (null, null);
        personasMinDistance = (null, null);

        double? max = null;
        double? min = null;

        foreach (var src in _lookup.Values)
        {
            foreach (var kv in src.distances)
            {
                var target = kv.Key;
                var d = kv.Value;

                if (double.IsInfinity(d) || double.IsNaN(d)) continue;
                if (src == target || d == 0) continue;

                if (!max.HasValue || d > max.Value)
                {
                    max = d;
                    personasMaxDistance = (src.familiar, target.familiar);
                }

                if (!min.HasValue || d < min.Value)
                {
                    min = d;
                    personasMinDistance = (src.familiar, target.familiar);
                }
            }
        }
    }

    //para debug y pruebas
    public void PrintEdges()
    {
        Console.WriteLine("Aristas (por nodo):");
        foreach (var n in _lookup.Values)
        {
            Console.WriteLine($" Nodo {n.familiar.name}:");
            foreach (var e in n.edges)
                Console.WriteLine($"   {e}");
        }
    }

    public void PrintDistanceMatrix()
    {
        Console.WriteLine("\nMatriz de distancias (metros):");
        foreach (var src in _lookup.Values)
        {
            Console.WriteLine($" Desde {src.familiar.name}:");
            foreach (var kv in src.distances)
            {
                var target = kv.Key;
                var d = kv.Value;
                var dstr = double.IsInfinity(d) ? "∞" : $"{d:F1}";
                Console.WriteLine($"   -> {target.familiar.name}: {dstr}");
            }
        }
    }
}

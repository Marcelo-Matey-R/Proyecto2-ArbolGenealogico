using System.Globalization;
using System.Globalization;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Microsoft.VisualBasic;
using Google.OpenLocationCode;
using Mapsui.Projections;
using Mapsui.Extensions;

//se necesita recolocar despues, para usar en UI
public interface IImageService
{
    ///Guarda el archivo subido en la carpeta de fotos y devuelve el photoFileName (relativo)
    string SaveUploadedPhoto(string sourceFilePath);

    ///Resuelve la ruta absoluta de un photoFileName relativo (o devuelve la absoluta si ya lo es)
    string ResolvePhotoPath(string photoFileName);

    ///Valida si el nombre/extension es de imagen permitida
    bool IsValidImageFileName(string fileName);
}


public abstract class NotifyBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        var handler = PropertyChanged;
        if (handler != null)
        {
            handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
class Persona : NotifyBase
{
    public Persona()
    {
        this.id = Guid.NewGuid();
        this.birthdate = DateTime.MinValue;
    }

    public Persona(string id, string Name, int Age, DateTime BirthDate, string photo = "",
    string Addres = "", double? Lon = null, double? Lat = null, Guid? ParentId = null)
    {
        this.id = Guid.NewGuid();
        this.ownId = id;
        this.name = Name;
        this.age = Age;
        this.birthdate = BirthDate;
        this.photoFileName = photo ?? "";
        this.addresPlusCode = Addres;
        this.lon = Lon;
        this.lat = Lat;
        this.parentId = ParentId;
    }
    public Persona(Guid id, string Ownid, string Name, int Age, DateTime BirthDate, string photo = "",
    string Addres = "", double? Lon = null, double? Lat = null, Guid? ParentId = null)
    {
        this.id = id;
        this.ownId = Ownid;
        this.name = Name;
        this.age = Age;
        this.birthdate = BirthDate;
        this.photoFileName = photo ?? "";
        this.addresPlusCode = Addres;
        this.lon = Lon;
        this.lat = Lat;
        this.parentId = ParentId;
    }
    [JsonInclude]
    public Guid id { get; private set; }
    private string _ownId = "";
    public string ownId
    {
        get => _ownId;
        set => SetProperty(ref _ownId, value);
    }

    private string _name;
    public string name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private int _age;
    public int age
    {
        get => _age;
        set => SetProperty(ref _age, value); 
    }
    private DateTime _birthdate;
    public DateTime birthdate
    {
        get => _birthdate;
        set => SetProperty(ref _birthdate, value); 
    }
    private Guid? _parentId;
    public Guid? parentId
    {
        get => _parentId;
        set => SetProperty(ref _parentId, value);
    }

    private string _urlImage = "";
    public string photoFileName
    {
        get => _urlImage;
        set => SetProperty(ref _urlImage, value);
    }

    private string _addresPlusCode = "";
    public string addresPlusCode
    {
        get => _addresPlusCode;
        set => SetProperty(ref _addresPlusCode, value);
    }

    private double? _lon;
    public double? lon
    {
        get => _lon;
        set => SetProperty(ref _lon, value);
    }

    private double? _lat;
    public double? lat
    {
        get => _lat;
        set =>  SetProperty(ref _lat, value); 
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
    public Persona familiar { get; }
    public string partner { get; set; }

    private Node? _parent;
    public Node? parent => _parent;
    private readonly List<Node> _children = new List<Node>();
    public IReadOnlyList<Node> children => _children.AsReadOnly();

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

        if (!_children.Contains(child))
        {
            child.DetachFromParent();
            child._parent = this;
            _children.Add(child);
            child.familiar.parentId = this.familiar.id;
        }
    }
    public void DetachFromParent()
    {
        if (_parent != null)
        {
            _parent._children.Remove(this);
            _parent = null;
            this.familiar.parentId = null;
        }
    }
    public int GetLevel()
    {
        int lvl = 0;
        var curr = this._parent;

        while (curr != null)
        {
            lvl++;
            curr = curr._parent;
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
    public Node fam1 { get; }
    public Node fam2 { get; }

    public double weight { get; }

    public Edge(Node per1, Node per2, double w)
    {
        this.fam1 = per1 ?? throw new ArgumentNullException(nameof(per1));
        this.fam2 = per2 ?? throw new ArgumentNullException(nameof(per2));

        this.weight = w;
    }
    public bool IsValid() => !double.IsNaN(weight) && !double.IsInfinity(weight);
    public override string ToString() => $"{fam1.familiar.name} -> {fam2.familiar.name}: {weight:F6}"; 

}

class CalcDistance
{
    //convierte plus code a Lon y Lat
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
    //devuelve la distancia en km, ussando la conversion de SphericalMarcator
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

        return (double)dis / 1000;
    }
    
    public bool TryDistanceIn(double? lon1, double? lat1, double? lon2, double? lat2, out double kiloMeters)
    {
        kiloMeters = Distance(lon1, lat1, lon2, lat2);
        return !(double.IsNaN(kiloMeters) || double.IsInfinity(kiloMeters));
    }
}



class TreeManager
{
    //token de sincronizacion
    private readonly object _sync = new object();

    //estructura de datos internas
    private readonly Dictionary<Guid, Node> _lookup = new Dictionary<Guid, Node>();
    private readonly List<Node> _roots = new List<Node>();
    private CalcDistance calcDistance = new CalcDistance();

    //acciones para la UI
    public event Action<Node>? nodeAdded;

    //el evento se define por hijo/nodo acutal, antiguo padre, nuevo padre
    public event Action<Guid, Guid?, Guid?>? changeParent;
    public event Action? graphChanged;

    //resultados
    public (Persona?, Persona?) personasMaxDistance = (null, null);
    public (Persona?, Persona?) personasMinDistance = (null, null);
    
    public IReadOnlyCollection<Node> Roots
    {
        get
        {
            lock(_sync){ return _roots.ToList().AsReadOnly(); }
        }
    }

    public Node? FindNodeById(Guid id)
    {
        lock(_sync){return _lookup.TryGetValue(id, out var n) ? n : null;}
    }

    public IReadOnlyList<Node> GetAllowedParents(Guid nodeId)
    {
        lock (_sync)
        {
            if (!_lookup.TryGetValue(nodeId, out var node)) return new List<Node>().AsReadOnly();

            // marcar descendientes
            var banned = new HashSet<Node>();
            node.TransverseDFS(n => banned.Add(n));

            // todos los nodos excepto los banneds
            var list = _lookup.Values.Where(n => !banned.Contains(n)).ToList();
            return list.AsReadOnly();
        }
    }

    public void BuildFromPersons(IEnumerable<Persona> people, Func<Persona, Guid?>? parentSelector = null)
    {
        if (people == null) throw new ArgumentNullException(nameof(people));

        var tempLookup = new Dictionary<Guid, Node>();
        var tempRoots = new List<Node>();


        // Crear nodo por persona
        foreach (var p in people)
            tempLookup[p.id] = new Node(p);

        // Enlazar
        foreach (var p in people)
        {
            Guid id = p.id;
            Guid? parentId = parentSelector?.Invoke(p) ?? p.parentId;

            if (parentId == null)
            {
                tempRoots.Add(tempLookup[id]);
            }
            else if (tempLookup.ContainsKey(parentId.Value))
            {
                var parentNode = tempLookup[parentId.Value];
                parentNode.AddChild(tempLookup[id]);
            }
            else
            {
                tempRoots.Add(tempLookup[id]);
            }
        }

        lock (_sync)
        {
            _lookup.Clear();
            _roots.Clear();

            foreach (var kv in tempLookup) _lookup[kv.Key] = kv.Value;
            _roots.AddRange(tempRoots);
        }

        UpdateGraphAndDistances();
    }

    public Node AddPerson(Persona p, Guid? parentId = null)
    {
        if (p == null) throw new ArgumentNullException(nameof(p));

        Node created;
        bool addAsRoot = false;

        lock (_sync)
        {
            if (_lookup.ContainsKey(p.id)) throw new InvalidOperationException($"Ya existe una persona con id={p.id}");

            var node = new Node(p);
            _lookup[p.id] = node;

            if (parentId == null)
            {
                _roots.Add(node);
                addAsRoot = true;
            }

            else
            {
                if (!_lookup.TryGetValue(parentId.Value, out var parentNode))
                {
                    _roots.Add(node);
                    addAsRoot = true;
                }
                else
                {
                    if (IsAncestor(descendant: parentNode, ancestorCandidate: node))
                        throw new InvalidOperationException("Operación inválida: el nuevo padre sería descendiente del nodo (crearía un ciclo).");

                    parentNode.AddChild(node);
                }
            }

            created = node;
        }
        nodeAdded?.Invoke(created);

        UpdateGraphAndDistances();

        return created;
    }
    public void ReassignParent(Guid childId, Guid? newParentId)
    {
        Node childNode;
        Node? newParentNode = null;
        Guid? oldParentId = null;

        lock (_sync)
        {
            if (!_lookup.TryGetValue(childId, out childNode))
                throw new ArgumentException($"Nodo hijo con id {childId} no existe.", nameof(childId));

            if (newParentId.HasValue)
            {
                if (!_lookup.TryGetValue(newParentId.Value, out newParentNode))
                    throw new ArgumentException($"Nuevo padre con id {newParentId.Value} no existe.", nameof(newParentId));
            }

            // self-parent check
            if (newParentNode != null && ReferenceEquals(newParentNode, childNode))
                throw new InvalidOperationException("Un nodo no puede ser padre de sí mismo.");

            // cycle check: subimos desde newParentNode y vemos si llegamos a childNode
            if (newParentNode != null)
            {
                var cur = newParentNode;
                while (cur != null)
                {
                    if (ReferenceEquals(cur, childNode))
                        throw new InvalidOperationException("Operación inválida: el nuevo padre sería descendiente del nodo (crearía un ciclo).");
                    cur = cur.parent;
                }
            }

            // guardar oldParentId para notificación posterior
            oldParentId = childNode.familiar.parentId;

            // detach del padre actual (si lo tiene) de forma segura
            childNode.DetachFromParent();

            // si newParentNode == null -> poner en roots; si no -> AddChild
            if (newParentNode == null)
            {
                // ya está detachado; añadir a roots si no está
                if (!_roots.Contains(childNode)) _roots.Add(childNode);
                childNode.familiar.parentId = null;
            }
            else
            {
                // si el child estaba como root, quitar la entrada de roots antes de añadirlo
                _roots.Remove(childNode);
                newParentNode.AddChild(childNode); // esto actualiza childNode._parent y childNode.familiar.parentId
            }
        } // fin lock

        // notificar fuera del lock
        changeParent?.Invoke(childId, oldParentId, newParentId);

        // recalcular grafo fuera del lock
        UpdateGraphAndDistances();
    }



    public void GetEdgesWithWeights()
    {
        List<Node> tempNodes;
        List<Node> tempRoots;

        lock (_sync)
        {
            tempNodes = _lookup.Values.ToList();
            tempRoots = _roots.ToList();
        }
    
        foreach (var node in tempNodes) node.edges.Clear();

        foreach (var root in tempRoots)
        {
            root.TransverseDFS(n =>
            {
                foreach (var child in n.children)
                {
                    var a = n;
                    var b = child;
                    double w = calcDistance.Distance(a.familiar.lon, a.familiar.lat, b.familiar.lon, b.familiar.lat); // devuelve NaN si falta coord
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

    public Dictionary<Node,double> Dijkstra(Node? source)
    {
        var dist = _lookup.Values.ToDictionary(x => x, x => double.PositiveInfinity);

        if (source == null) return dist;

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
        List<Node> nodes;
        lock (_sync)
        {
            nodes = _lookup.Values.ToList();
        }
        foreach (var n in nodes)
            n.distances = Dijkstra(n);
    }

    private void UpdateGraphAndDistances()
    {
        GetEdgesWithWeights();

        ComputeAllDijkstras();

        ComputeMinMaxPairs();

        graphChanged?.Invoke();
    }
    public void ComputeMinMaxPairs()
    {
        personasMaxDistance = (null, null);
        personasMinDistance = (null, null);

        double? max = null;
        double? min = null;

        List<Node> nodes;
        lock (_sync) nodes = _lookup.Values.ToList();

        foreach (var src in nodes)
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
    public IEnumerable<PersonDto> ExportToDto()
    {
        lock (_sync)
        {
            return _lookup.Values.Select(n => new PersonDto
            {
                Id = n.familiar.id,
                Nombre = n.familiar.name,
                ParentId = n.familiar.parentId,
                Latitude = n.familiar.lat,
                Longitude = n.familiar.lon,
                PhotoFileName = n.familiar.photoFileName,
                ExternalId = n.familiar.ownId
            }).ToList();
        }
    }

    public void ImportFromDto(IEnumerable<PersonDto> dtos)
    {
        if (dtos == null) throw new ArgumentNullException(nameof(dtos));
        var list = dtos.ToList();

        var persons = list.Select(d => new Persona(
            d.Id,
            d.ExternalId ?? string.Empty,
            d.Nombre ?? string.Empty,
            0,
            DateTime.MinValue,
            photo: d.PhotoFileName ?? string.Empty,
            Addres: "",
            Lon: d.Longitude,
            Lat: d.Latitude,
            ParentId: d.ParentId
        )).ToList();

        BuildFromPersons(persons, null);
    }
    public class PersonDto
    {
        public Guid Id { get; set; }
        public string? Nombre { get; set; }
        public Guid? ParentId { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? PhotoFileName { get; set; }
        public string? PhotoBase64 { get; set; }
        public string? UrlImage { get; set; }
        public string? ExternalId { get; set; }
    }
}

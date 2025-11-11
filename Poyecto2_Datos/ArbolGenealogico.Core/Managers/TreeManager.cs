
using System.Globalization;
using ArbolGenealogico.Domain.Models;
using ArbolGenealogico.Infraestructure.Services;
using ArbolGenealogico.Core.Events;
using ArbolGenealogico.Core.Mappers;
using ArbolGenealogico.Domain.Dto;

namespace ArbolGenealogico.Core.Managers
{
    public class TreeManager
    {
        //token de sincronizacion
        private readonly object _sync = new object();

        //estructura de datos internas
        private readonly Dictionary<Guid, Node> _lookup = new Dictionary<Guid, Node>();
        private readonly List<Node> _roots = new List<Node>();
        private CalcDistance _calcDistance;

        //acciones para la UI
        public event Action<Node>? nodeAdded;

        //el evento se define por hijo/nodo acutal, antiguo padre, nuevo padre
        public event EventHandler<ParentChangedEventArgs>? changeParent;
        public event EventHandler<PartnerChangedEventArgs>? changePartner;
        public event EventHandler<Node>? NodeAdded;
        public event EventHandler? graphChanged;


        //resultados
        public (Persona?, Persona?) personasMaxDistance = (null, null);
        public (Persona?, Persona?) personasMinDistance = (null, null);
        public double averageDistances = 0;

        public IReadOnlyCollection<Node> Roots
        {
            get
            {
                lock (_sync) { return _roots.ToList().AsReadOnly(); }
            }
        }

        public TreeManager()
        {
            _calcDistance = new CalcDistance();
        }

        public Node? FindNodeById(Guid id)
        {
            lock (_sync) { return _lookup.TryGetValue(id, out var n) ? n : null; }
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
            NodeAdded?.Invoke(this, created);

            UpdateGraphAndDistances();

            return created;
        }
        public void SetPartner(Guid idA, Guid? idB)
        {
            bool changed = false;
            Guid? oldPartnerId = null;
            Guid? newPartnerId = null;

            lock (_sync)
            {
                if (!_lookup.TryGetValue(idA, out var nodeA))
                    throw new ArgumentException("Persona A no existe", nameof(idA));

                oldPartnerId = nodeA.familiar.partnerId;
                // Caso: desasociar A (idB == null)
                if (!idB.HasValue)
                {
                    if (oldPartnerId.HasValue)
                    {
                        // si hay pareja previa, desasociar mutuamente (si existe en lookup)
                        if (_lookup.TryGetValue(oldPartnerId.Value, out var oldPartnerNode))
                            nodeA.DetachFromPartner(oldPartnerNode);
                        else
                            nodeA.DetachFromPartner(null);

                        changed = true;
                        newPartnerId = nodeA.familiar.partnerId; // debería ser null
                    }
                    // si no tenía pareja -> no hay cambio
                    return;
                }

                // Caso: asociar A con B -> validamos B UNA vez
                if (!_lookup.TryGetValue(idB.Value, out var nodeB))
                    throw new ArgumentException("Persona B no existe", nameof(idB));

                if (ReferenceEquals(nodeA, nodeB))
                    throw new InvalidOperationException("Una persona no puede ser pareja de sí misma.");

                if (nodeA.familiar.partnerId.HasValue
                    && nodeA.familiar.partnerId.Value == nodeB.familiar.id
                    && nodeB.familiar.partnerId.HasValue
                    && nodeB.familiar.partnerId.Value == nodeA.familiar.id)
                {
                    // nada que hacer
                    return;
                }

                // Limpiar parejas previas de A (si las tiene)
                if (nodeA.familiar.partnerId.HasValue)
                {
                    if (_lookup.TryGetValue(nodeA.familiar.partnerId.Value, out var aOld))
                        nodeA.DetachFromPartner(aOld);
                    else
                        nodeA.DetachFromPartner(null);
                }

                // Limpiar parejas previas de B (si las tiene)
                if (nodeB.familiar.partnerId.HasValue)
                {
                    if (_lookup.TryGetValue(nodeB.familiar.partnerId.Value, out var bOld))
                        nodeB.DetachFromPartner(bOld);

                    else
                        nodeB.DetachFromPartner(null);
                }

                // Atar A <-> B (helper en Node)
                nodeA.AttachPartner(nodeB);
                changed = true;
                newPartnerId = nodeA.familiar.partnerId;
            } // fin lock

            if (changed)
            {
                UpdateGraphAndDistances();
                changePartner?.Invoke(this, new PartnerChangedEventArgs(idA, oldPartnerId, newPartnerId));
            }
        }
        public Node? GetPartnerNode(Node n)
        {
            if (n?.familiar?.partnerId == null) return null;
            var pid = n.familiar.partnerId.Value;
            lock (_sync)
            {
                return _lookup.TryGetValue(pid, out var partnerNode) ? partnerNode : null;
            }
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
            changeParent?.Invoke(this, new ParentChangedEventArgs(childId, oldParentId, newParentId));

            // recalcular grafo fuera del lock
            UpdateGraphAndDistances();
        }



        public void GetEdgesWithWeights()
        {
            List<Node> tempNodes;
            List<Node> tempRoots;
            Dictionary<(Guid, Guid), double> average = new Dictionary<(Guid, Guid), double>();

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
                        if (n.familiar.excludeFromDistance || child.familiar.excludeFromDistance) continue;
                        if (n.familiar.partnerId.HasValue && n.familiar.partnerId == child.familiar.id) continue;

                        double w = _calcDistance.Distance(n.familiar.lon, n.familiar.lat, child.familiar.lon, child.familiar.lat); // devuelve NaN si falta coord
                        if (double.IsNaN(w) || double.IsInfinity(w)) continue;

                        var e1 = new Edge(n, child, w);
                        var e2 = new Edge(child, n, w);
                        n.edges.Add(e1);
                        child.edges.Add(e2);

                        var idA = n.familiar.id;
                        var idB = child.familiar.id;
                        (Guid, Guid) key = idA.CompareTo(idB) <= 0 ? (idA, idB) : (idB, idA);

                        if (!average.ContainsKey(key))
                        {
                            average[key] = w;
                        }
                    }
                });
            }

            // Proteger Average() contra secuencia vacía
            if (average.Count > 0)
                averageDistances = average.Values.Average();
            else
                averageDistances = 0.0;
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

        public Dictionary<Node, double> Dijkstra(Node? source)
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

            graphChanged?.Invoke(this, EventArgs.Empty);
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
        public IEnumerable<PersonDto> ExportToDto(PersonMapper? mapper = null)
        {
            var m = mapper ?? new PersonMapper();
            lock (_sync)
            {
                // materializar para evitar problemas de concurrencia
                return _lookup.Values.Select(n => m.ToDto(n.familiar)).ToList();
            }

        }

        public void ImportFromDto(IEnumerable<PersonDto> dtos, PersonMapper? mapper = null)
        {
            if (dtos == null) throw new ArgumentNullException(nameof(dtos));
            var list = dtos.ToList();
            var m = mapper ?? new PersonMapper();

            // 1) crear nodos temporales (sin relaciones)
            var tempLookup = new Dictionary<Guid, Node>(list.Count);
            foreach (var d in list)
            {
                var persona = m.FromDto(d);
                var node = new Node(persona);
                tempLookup[persona.id] = node;
            }

            // 2) enlazar padres (si el parent está presente), o marcar como root
            var tempRoots = new List<Node>();
            foreach (var node in tempLookup.Values)
            {
                var parentId = node.familiar.parentId;
                if (!parentId.HasValue)
                {
                    tempRoots.Add(node);
                    continue;
                }
                if (tempLookup.TryGetValue(parentId.Value, out var parentNode))
                {
                    parentNode.AddChild(node);
                }
                else
                {
                    // parent no presente en DTOs => considerar como root (policy elegida)
                    tempRoots.Add(node);
                }
            }

            // 3) enlazar parejas (hacerlo después para asegurar existencia de nodos)
            var paired = new HashSet<Guid>();
            foreach (var node in tempLookup.Values)
            {
                var pid = node.familiar.partnerId;
                if (!pid.HasValue) continue;
                if (paired.Contains(node.familiar.id)) continue;

                if (tempLookup.TryGetValue(pid.Value, out var partnerNode))
                {
                    // Solo attach si no están ya emparejados correctamente
                    if (!(node.familiar.partnerId.HasValue &&
                        partnerNode.familiar.partnerId.HasValue &&
                        node.familiar.partnerId.Value == partnerNode.familiar.id &&
                        partnerNode.familiar.partnerId.Value == node.familiar.id))
                    {
                        node.AttachPartner(partnerNode);
                    }
                    paired.Add(node.familiar.id);
                    paired.Add(partnerNode.familiar.id);
                }
                // si el partner no está en tempLookup dejamos el partnerId tal cual (referencia externa)
            }

            // 4) swap atómico en el manager
            lock (_sync)
            {
                _lookup.Clear();
                _roots.Clear();
                foreach (var kv in tempLookup) _lookup[kv.Key] = kv.Value;
                _roots.AddRange(tempRoots);
            }

            UpdateGraphAndDistances();
        }

    }
}

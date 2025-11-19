using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ArbolGenealogico.Domain.Models;
using ArbolGenealogico.Infraestructure.Services;
using ArbolGenealogico.Core.Events;
using ArbolGenealogico.Core.Mappers;
using ArbolGenealogico.Domain.Dto;

namespace ArbolGenealogico.Core.Managers
{
    public class NodeEventArgs : EventArgs
    {
        public Node Node { get; }
        public NodeEventArgs(Node node) { Node = node; }
    }

    public class TreeManager
    {
        #region Fields (sincronización y datos internos)

        // token de sincronizacion
        private readonly object _sync = new object();
        private int _suspendCount = 0;
        private bool _pendingRecalc = false;

        // estructura de datos internas
        private readonly Dictionary<Guid, Node> _lookup = new Dictionary<Guid, Node>();
        private readonly List<Node> _roots = new List<Node>();
        private CalcDistance _calcDistance;

#if DEBUG
        // solo para debug en compilación Debug
        private readonly List<string> _debugPairs = new List<string>();
#endif

        #endregion

        #region Eventos

        // eventos para la UI
        public event EventHandler<NodeEventArgs>? NodeAdded;

        // eventos de cambios específicos
        public event EventHandler<ParentChangedEventArgs>? changeParent;
        public event EventHandler<PartnerChangedEventArgs>? changePartner;
        public event EventHandler? graphChanged;

        #endregion

        #region Resultados expuestos / Propiedades

        // resultados expuestos
        public (Persona?, Persona?) personasMaxDistance = (null, null);
        public double? maxDistance = null;
        public (Persona?, Persona?) personasMinDistance = (null, null);
        public double? minDistance = null;
        public double averageDistances = 0;

        public List<PairGeoInfo> pairsGeoInfoDistance = new List<PairGeoInfo>();

        public IReadOnlyCollection<Node> Roots
        {
            get
            {
                lock (_sync) { return _roots.ToList().AsReadOnly(); }
            }
        }

        #endregion

        #region Constructor

        public TreeManager()
        {
            _calcDistance = new CalcDistance();
        }

        #endregion

        #region Operaciones de búsqueda / consulta (públicas)

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

            lock (_sync)
            {
                if (_lookup.ContainsKey(p.id)) throw new InvalidOperationException($"Ya existe una persona con id={p.id}");

                var node = new Node(p);
                _lookup[p.id] = node;

                if (parentId == null)
                {
                    _roots.Add(node);
                    node.familiar.parentId = null; // mantener consistente el estado
                }
                else
                {
                    if (!_lookup.TryGetValue(parentId.Value, out var parentNode))
                    {
                        _roots.Add(node);
                        node.familiar.parentId = null; // parent no existe -> root y parentId null
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

            NodeAdded?.Invoke(this, new NodeEventArgs(created));

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
                }
                else
                {
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
                        // ya emparejados correctamente -> nada que hacer
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

                    if (IsAncestor(descendant: nodeA, ancestorCandidate: nodeB) ||
                        IsAncestor(descendant: nodeB, ancestorCandidate: nodeA))
                    {
                        throw new InvalidOperationException("Operación inválida: no se puede emparejar a un nodo con su ancestro/descendiente.");
                    }

                    // Atar A <-> B (helper en Node)
                    nodeA.AttachPartner(nodeB);
                    changed = true;
                    newPartnerId = nodeA.familiar.partnerId;
                }
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

        public void BeginUpdate()
        {
            lock (_sync) { _suspendCount++; }
        }

        public void EndUpdate()
        {
            bool doRecalc = false;
            lock (_sync)
            {
                if (_suspendCount > 0) _suspendCount--;
                if (_suspendCount == 0 && _pendingRecalc)
                {
                    _pendingRecalc = false;
                    doRecalc = true;
                }
            }
            if (doRecalc) UpdateGraphAndDistances();
        }

        #endregion

        #region Export/Import / Utilidades públicas

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

        #endregion

        #region Cálculos de grafo / rutas y distancias

        public void GetEdgesWithWeights()
        {
            List<Node> tempNodes;
            List<Node> tempRoots;

            lock (_sync)
            {
                tempNodes = _lookup.Values.ToList();
                tempRoots = _roots.ToList();
            }

            // limpiar listas de aristas
            foreach (var node in tempNodes) node.edges.Clear();

            // conjunto para evitar añadir la misma arista dos veces (canonizar orden de guids)
            var edgesAdded = new HashSet<(Guid, Guid)>();

            foreach (var root in tempRoots.OrderBy(r => r.familiar.id))
            {
                root.TransverseDFS(n =>
                {
                    foreach (var child in n.children)
                    {
                        if (n.familiar.excludeFromDistance || child.familiar.excludeFromDistance) return;
                        // no saltar por ser pareja: tratamos cada relación explícitamente,
                        // y deduplicamos con edgesAdded para que no haya aristas repetidas
                        double w = _calcDistance.Distance(n.familiar.lon, n.familiar.lat, child.familiar.lon, child.familiar.lat);
                        if (double.IsNaN(w) || double.IsInfinity(w)) return;

                        var idA = n.familiar.id;
                        var idB = child.familiar.id;
                        var key = idA.CompareTo(idB) <= 0 ? (idA, idB) : (idB, idA);

                        if (!edgesAdded.Add(key))
                        {
                            // ya añadida -> no volver a crear aristas
                            return;
                        }

                        var e1 = new Edge(n, child, w);
                        var e2 = new Edge(child, n, w);
                        n.edges.Add(e1);
                        child.edges.Add(e2);
                    }
                });
            }

            // partners: iterar sobre nodos en orden determinista
            var idMap = tempNodes.ToDictionary(x => x.familiar.id, x => x);
            foreach (var n in tempNodes.OrderBy(x => x.familiar.id))
            {
                if (!n.familiar.partnerId.HasValue) continue;
                var pid = n.familiar.partnerId.Value;
                if (!idMap.TryGetValue(pid, out var partnerNode)) continue;

                // canonical key
                var idA = n.familiar.id;
                var idB = partnerNode.familiar.id;
                var key = idA.CompareTo(idB) <= 0 ? (idA, idB) : (idB, idA);

                // si ya se añadió esa arista (parent-child o partner previo), saltamos
                if (!edgesAdded.Add(key)) continue;

                if (n.familiar.excludeFromDistance || partnerNode.familiar.excludeFromDistance) continue;

                double w = _calcDistance.Distance(n.familiar.lon, n.familiar.lat, partnerNode.familiar.lon, partnerNode.familiar.lat);
                if (double.IsNaN(w) || double.IsInfinity(w)) continue;

                var e1p = new Edge(n, partnerNode, w);
                var e2p = new Edge(partnerNode, n, w);
                n.edges.Add(e1p);
                partnerNode.edges.Add(e2p);
            }
        }

        public Dictionary<Node, double> Dijkstra(Node? source)
        {
            List<Node> snapshot;
            lock (_sync) { snapshot = _lookup.Values.ToList(); }

            var dist = snapshot.ToDictionary(x => x, x => double.PositiveInfinity);

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

        private void ComputeAverageShortestPathDistance()
        {
            double sum = 0.0;
            long count = 0;

            List<Node> nodes;
            lock (_sync)
            {
                nodes = _lookup.Values.ToList();
            }

            // usamos un HashSet de pares canónicos para garantizar que contamos cada pareja unordered una sola vez
            var counted = new HashSet<(Guid, Guid)>();

            foreach (var src in nodes)
            {
                foreach (var kv in src.distances)
                {
                    var dst = kv.Key;
                    var d = kv.Value;

                    if (double.IsInfinity(d) || double.IsNaN(d)) continue;
                    if (src == dst || d == 0) continue; // saltar identidad

                    // canonical key (minId, maxId)
                    var idA = src.familiar.id;
                    var idB = dst.familiar.id;
                    var key = idA.CompareTo(idB) <= 0 ? (idA, idB) : (idB, idA);

                    // si ya contamos este par, lo ignoramos
                    if (!counted.Add(key)) continue;

                    // sumar primero y luego registrar la traza (evita "suma desfasada")
                    sum += d;
                    count++;

#if DEBUG
                    lock (_sync)
                    {
                        _debugPairs.Add($"{src.familiar.name} <-> {dst.familiar.name} = {d}\nla suma es de = {sum} y  es el numero {count}\n");
                    }
#endif
                }
            }

            averageDistances = (count > 0) ? (sum / count) : 0.0;
        }

        // Refactorizado: Orquestador para obtener pares geodésicos (usa helpers privados)
        public void GetAllPairGeodesicDistances()
        {
            var result = new List<PairGeoInfo>();

            // 1) snapshot + recolección de nodos válidos
            var nodes = CollectNodesForGeodesic();

            var seen = new HashSet<(Guid, Guid)>();

            // 2) dobles bucles (i < j) para pares únicos
            for (int i = 0; i < nodes.Count; i++)
            {
                for (int j = i + 1; j < nodes.Count; j++)
                {
                    var nA = nodes[i];
                    var nB = nodes[j];
                    if (nA == null || nB == null) continue;

                    var a = nA.familiar;
                    var b = nB.familiar;
                    if (a == null || b == null) continue;
                    if (a.excludeFromDistance || b.excludeFromDistance) continue;

                    var key = a.id.CompareTo(b.id) <= 0 ? (a.id, b.id) : (b.id, a.id);
                    if (seen.Contains(key)) continue;
                    seen.Add(key);

                    // crear objeto PairGeoInfo (con manejo de excepciones y NaN)
                    var info = CreatePairGeoInfo(nA, nB);

                    result.Add(info);

                    // añadir distancia al diccionario de distancias de cada nodo (si no existe)
                    AddPairDistanceToNodes(nA, nB, info.DistanceKm);
                }
            }

            // 3) swap atómico del resultado
            lock (_sync)
            {
                pairsGeoInfoDistance = result;
            }
        }

        // Helper 1: snapshot + recolección de nodos desde raíces
        private List<Node> CollectNodesForGeodesic()
        {
            List<Node> rootsSnapshot;
            lock (_sync)
            {
                rootsSnapshot = _roots.ToList();
            }

            var nodes = rootsSnapshot
                .SelectMany(r =>
                {
                    var tmp = new List<Node>();
                    r.TransverseDFS(n => tmp.Add(n));
                    return tmp;
                })
                .Distinct()
                .Where(n => n?.familiar != null && n.familiar.excludeFromDistance == false)
                .ToList();

            return nodes;
        }

        // Helper 2: crear PairGeoInfo para dos nodos (maneja NaN/Infinity y excepciones)
        private PairGeoInfo CreatePairGeoInfo(Node nA, Node nB)
        {
            var a = nA.familiar;
            var b = nB.familiar;

            var info = new PairGeoInfo
            {
                AId = a.id,
                BId = b.id,
                AName = a.name,
                BName = b.name,
                DistanceKm = double.NaN
            };

            try
            {
                // mismo orden que tenías: lon, lat, lon, lat
                var d = _calcDistance.Distance(a.lon, a.lat, b.lon, b.lat);
                info.DistanceKm = double.IsNaN(d) || double.IsInfinity(d) ? double.NaN : d;
            }
            catch
            {
                info.DistanceKm = double.NaN;
            }

            return info;
        }

        // Helper 3: añadir entradas a los diccionarios de distancias de ambos nodos (si no existen)
        private void AddPairDistanceToNodes(Node nA, Node nB, double distance)
        {
            // Mantener la misma semántica que tenías: sólo añadir si no existe la clave
            if (!nA.distances.ContainsKey(nB))
                nA.distances.Add(nB, distance);

            if (!nB.distances.ContainsKey(nA))
                nB.distances.Add(nA, distance);
        }

        // Calcula el promedio geodésico (km) usando los pares devueltos por GetAllPairGeodesicDistances.
        // Devuelve double.NaN si no hay pares válidos.
        public void ComputeAverageGeodesic()
        {
            var pairs = pairsGeoInfoDistance;
            double sum = 0.0;
            long count = 0;

            foreach (var p in pairs)
            {
                if (!double.IsNaN(p.DistanceKm) && !double.IsInfinity(p.DistanceKm))
                {
                    sum += p.DistanceKm;
                    count++;
                }
            }

            averageDistances = (count > 0) ? (sum / count) : 0.0;
        }

        public void ComputeMinMaxPairs()
        {
            // inicializar valores
            (Persona?, Persona?) maxPair = (null, null);
            (Persona?, Persona?) minPair = (null, null);
            double? max = null;
            double? min = null;

            List<Node> nodes;
            lock (_sync)
            {
                nodes = _lookup.Values.ToList();
            }

            // Si no hay nodos suficientes, dejar nulls y salir
            if (nodes == null || nodes.Count < 2)
            {
                personasMaxDistance = (null, null);
                personasMinDistance = (null, null);
                return;
            }

            foreach (var src in nodes)
            {
                // proteger caso distances nulo
                if (src.distances == null) continue;

                foreach (var kv in src.distances)
                {
                    var target = kv.Key;
                    var d = kv.Value;

                    if (double.IsInfinity(d) || double.IsNaN(d)) continue;
                    if (src == target || d == 0) continue;

                    // actualizar max
                    if (!max.HasValue || d > max.Value)
                    {
                        max = d;
                        maxPair = (src.familiar, target.familiar);
                    }

                    // actualizar min
                    if (!min.HasValue || d < min.Value)
                    {
                        min = d;
                        minPair = (src.familiar, target.familiar);
                    }
                }
            }

            // aplicar resultados de forma atómica (lock por seguridad)
            lock (_sync)
            {
                personasMaxDistance = maxPair;
                personasMinDistance = minPair;
            }
        }

        public void ComputeGeodesicMinMaxPairs()
        {
            // Inicializar valores locales
            (Persona?, Persona?) maxPair = (null, null);
            (Persona?, Persona?) minPair = (null, null);
            double? max = null;
            double? min = null;

            // Tomar snapshot seguro de nodos
            var pairs = pairsGeoInfoDistance;

            foreach (var pair in pairs)
            {
                var d = pair.DistanceKm;
                var per1 = FindNodeById(pair.AId).familiar;
                var per2 = FindNodeById(pair.BId).familiar;

                if (double.IsInfinity(d) || double.IsNaN(d)) continue;
                if (d == 0 || per1 == null || per2 == null) continue;

                if (!max.HasValue || max < d)
                {
                    max = d;
                    maxPair = (per1, per2);
                }
                if (!min.HasValue || min > d)
                {
                    min = d;
                    minPair = (per1, per2);
                }
            }

            // Aplicar de forma atómica los resultados
            lock (_sync)
            {
                personasMaxDistance = maxPair;
                personasMinDistance = minPair;
                maxDistance = max;
                minDistance = min;
            }
        }

        #endregion

        #region Actualización del grafo (flujo principal)

        private void RequestRecalc()
        {
            bool doRecalc = false;
            lock (_sync)
            {
                if (_suspendCount > 0)
                {
                    _pendingRecalc = true;
                    return;
                }
                // no suspendido -> ejecutar fuera del lock
                doRecalc = true;
            }
            if (doRecalc) UpdateGraphAndDistances();
        }

        private void UpdateGraphAndDistances()
        {
            //Console.WriteLine($"UpdateGraphAndDistances IN({DateTime.UtcNow:HH:mm:ss.fff}) - Thread {Thread.CurrentThread.ManagedThreadId}");

            GetEdgesWithWeights();

            //ComputeAllDijkstras();

            //ComputeAverageShortestPathDistance();

            GetAllPairGeodesicDistances();

            ComputeAverageGeodesic();

            ComputeGeodesicMinMaxPairs();

            graphChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Debug / impresión

        // para debug y pruebas
        public void PrintEdges()
        {
            List<Node> nodes;
            lock (_sync) nodes = _lookup.Values.ToList();

            Console.WriteLine("Aristas (por nodo):");
            foreach (var n in nodes)
            {
                Console.WriteLine($" Nodo {n.familiar.name}:");
                foreach (var e in n.edges)
                    Console.WriteLine($"   {e}");
            }
        }

        public void PrintDistanceMatrix()
        {
            List<Node> nodes;
            lock (_sync) nodes = _lookup.Values.ToList();

            Console.WriteLine("\nMatriz de distancias (metros):");
            foreach (var src in nodes)
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

        #endregion

        #region Helper privado

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

        #endregion
    }
}

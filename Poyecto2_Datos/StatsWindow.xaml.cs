using ArbolGenealogico.Core.Managers;
using ArbolGenealogico.Domain.Models;
using ArbolGenealogico.Infraestructure.Services;
using System.Windows;

namespace Poyecto2_Datos
{
    public partial class StatsWindow : Window
    {
        private TreeManager? _treeManager;

        public StatsWindow()
        {
            InitializeComponent();

            _treeManager = ResolveTreeManager();
            if (_treeManager != null)
                _treeManager.graphChanged += TreeManager_graphChanged;

            RefreshAll();
        }

        #region Resolve TreeManager
        private TreeManager? ResolveTreeManager()
        {
            try
            {
                if (Application.Current?.Properties != null && Application.Current.Properties.Contains("TreeManager"))
                {
                    return Application.Current.Properties["TreeManager"] as TreeManager;
                }

                var tm = Activator.CreateInstance<TreeManager>();
                if (Application.Current?.Properties != null)
                    Application.Current.Properties["TreeManager"] = tm;
                return tm;
            }
            catch
            {
                return null;
            }
        }
        #endregion

        private void TreeManager_graphChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() => RefreshAll());
        }

        /// <summary>
        /// Punto central para refrescar toda la UI de estadísticas.
        /// Hace recalculos si es necesario y obtiene la lista de pares excluyendo
        /// personas marcadas con excludeFromDistance.
        /// </summary>
        private void RefreshAll()
        {
            try
            {
                if (_treeManager == null)
                {
                    SetNoTreeManagerUI();
                    return;
                }

                // Asegurar que las estructuras de distancia están actualizadas
                RecalculateGraphIfNeeded();

                // Obtener personas *incluidas* (filtradas)
                var includedNodes = GetIncludedNodes();
                var includedPersonas = includedNodes.Select(n => n.familiar).Where(p => p != null).Cast<Persona>().ToList();

                // 1) Par más lejano y distancia (calculado entre personas incluidas)
                if (includedPersonas.Count >= 2)
                {
                    var farPair = ComputeExtremePair(includedPersonas, findMax: true);
                    if (farPair != null)
                    {
                        TxtFarthestPair.Text = $"{farPair.Item1.name} ↔ {farPair.Item2.name}";
                        var dFar = GetSafeDistance(farPair.Item1, farPair.Item2);
                        TxtFarthestDistance.Text = (!double.IsNaN(dFar) && !double.IsInfinity(dFar))
                            ? $"{dFar:F3} km ({KmToMeters(dFar):F0} m)"
                            : "(sin datos)";
                    }
                    else
                    {
                        TxtFarthestPair.Text = "(sin datos)";
                        TxtFarthestDistance.Text = "";
                    }

                    var closePair = ComputeExtremePair(includedPersonas, findMax: false);
                    if (closePair != null)
                    {
                        TxtClosestPair.Text = $"{closePair.Item1.name} ↔ {closePair.Item2.name}";
                        var dClose = GetSafeDistance(closePair.Item1, closePair.Item2);
                        TxtClosestDistance.Text = (!double.IsNaN(dClose) && !double.IsInfinity(dClose))
                            ? $"{dClose:F3} km ({KmToMeters(dClose):F0} m)"
                            : "(sin datos)";
                    }
                    else
                    {
                        TxtClosestPair.Text = "(sin datos)";
                        TxtClosestDistance.Text = "";
                    }
                }
                else
                {
                    TxtFarthestPair.Text = "(sin datos)";
                    TxtFarthestDistance.Text = "";
                    TxtClosestPair.Text = "(sin datos)";
                    TxtClosestDistance.Text = "";
                }

                // 3) Distancia promedio (sobre pares incluidos)
                var avg = ComputeAverageDistance(includedPersonas);
                TxtAverageDistance.Text = (!double.IsNaN(avg) && !double.IsInfinity(avg)) ? $"{avg:F3} km" : "(sin datos)";

                // 4) Llenar DataGrid con las parejas (omitiendo excluidos)
                var pairs = BuildAllPairsList(includedNodes);
                DgPairs.ItemsSource = pairs.OrderByDescending(p => p.DistanceNumeric).ThenByDescending(p => p.DistanceKm).ToList();
                LblCount.Text = $"Pares listados: {pairs.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al refrescar estadísticas: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetNoTreeManagerUI()
        {
            TxtFarthestPair.Text = "(TreeManager no disponible)";
            TxtClosestPair.Text = "(TreeManager no disponible)";
            TxtAverageDistance.Text = "(TreeManager no disponible)";
            DgPairs.ItemsSource = null;
            LblCount.Text = "";
        }

        #region Distance helpers

        // Intenta usar distancias precomputadas (Dijkstra) si existen en los nodos,
        // sino calcula geodésicamente con CalcDistance (devuelve km).
        private double GetDistanceBetween(Persona? a, Persona? b)
        {
            if (a == null || b == null) return double.NaN;

            try
            {
                var nodeA = _treeManager?.FindNodeById(a.id);
                var nodeB = _treeManager?.FindNodeById(b.id);
                if (nodeA != null && nodeB != null && nodeA.distances != null && nodeA.distances.ContainsKey(nodeB))
                    return nodeA.distances[nodeB];
            }
            catch { /* ignorar y calcular directo */ }

            try
            {
                var calc = new CalcDistance();
                return calc.Distance(a.lon, a.lat, b.lon, b.lat);
            }
            catch
            {
                return double.NaN;
            }
        }

        // Devuelve distancia (km) pero si es NaN/Infinity y las dos personas son pareja -> devuelve 0.0
        private double GetSafeDistance(Persona? a, Persona? b)
        {
            var d = GetDistanceBetween(a, b);
            if (!double.IsNaN(d) && !double.IsInfinity(d)) return d;

            try
            {
                if (a != null && b != null)
                {
                    bool arePartners = (a.partnerId.HasValue && a.partnerId.Value == b.id)
                                        || (b.partnerId.HasValue && b.partnerId.Value == a.id);
                    if (arePartners) return 0.0;
                }
            }
            catch { /* ignore */ }

            return d;
        }

        private double KmToMeters(double km) => km * 1000.0;

        #endregion

        #region Node inclusion/filtering

        /// <summary>
        /// Recolecta y devuelve todos los nodos del árbol.
        /// </summary>
        private List<Node> GetIncludedNodes()
        {
            var list = new List<Node>();
            if (_treeManager == null) return list;

            foreach (var root in _treeManager.Roots)
            {
                root.TransverseDFS(n =>
                {
                    if (n != null && n.familiar != null)
                        list.Add(n);
                });
            }

            return list.Distinct().ToList();
        }

        #endregion

        #region Compute extremes & averages (based on included personas)

        // Devuelve el par (a,b) con distancia máxima o mínima entre una lista de personas (null si no hay)
        private Tuple<Persona, Persona>? ComputeExtremePair(List<Persona> personas, bool findMax)
        {
            if (personas == null || personas.Count < 2) return null;

            double bestValue = findMax ? double.MinValue : double.MaxValue;
            Tuple<Persona, Persona>? bestPair = null;

            for (int i = 0; i < personas.Count; i++)
            {
                for (int j = i + 1; j < personas.Count; j++)
                {
                    var a = personas[i];
                    var b = personas[j];
                    var d = GetSafeDistance(a, b);

                    if (double.IsNaN(d)) continue;

                    if (findMax)
                    {
                        if (d > bestValue)
                        {
                            bestValue = d;
                            bestPair = Tuple.Create(a, b);
                        }
                    }
                    else
                    {
                        if (d < bestValue)
                        {
                            bestValue = d;
                            bestPair = Tuple.Create(a, b);
                        }
                    }
                }
            }

            return bestPair;
        }

        // Promedio sobre todas las parejas (excluye NaN)
        private double ComputeAverageDistance(List<Persona> personas)
        {
            if (personas == null || personas.Count < 2) return double.NaN;

            double sum = 0.0;
            int count = 0;
            for (int i = 0; i < personas.Count; i++)
            {
                for (int j = i + 1; j < personas.Count; j++)
                {
                    var d = GetSafeDistance(personas[i], personas[j]);
                    if (!double.IsNaN(d) && !double.IsInfinity(d))
                    {
                        sum += d;
                        count++;
                    }
                }
            }

            return count > 0 ? (sum / count) : double.NaN;
        }

        #endregion

        #region Pairs list (DataGrid)

        /// <summary>
        /// Construye la lista de pares para el DataGrid usando únicamente nodos incluidos.
        /// Devuelve objetos DTO que contienen también la distancia numérica para ordenamiento.
        /// </summary>
        private List<PairDistanceDto> BuildAllPairsList(List<Node> includedNodes)
        {
            var list = new List<PairDistanceDto>();
            if (includedNodes == null || includedNodes.Count < 2) return list;

            // Usar HashSet para evitar duplicados unordered
            var seen = new HashSet<(Guid, Guid)>();

            for (int i = 0; i < includedNodes.Count; i++)
            {
                for (int j = i + 1; j < includedNodes.Count; j++)
                {
                    var a = includedNodes[i].familiar;
                    var b = includedNodes[j].familiar;
                    if (a == null || b == null) continue;


                    var key = a.id.CompareTo(b.id) <= 0 ? (a.id, b.id) : (b.id, a.id);
                    if (seen.Contains(key)) continue;
                    seen.Add(key);

                    var d = GetSafeDistance(a, b);
                    string kmStr, mStr;
                    if (!double.IsNaN(d) && !double.IsInfinity(d))
                    {
                        kmStr = d.ToString("F6");
                        mStr = (d * 1000.0).ToString("F1");
                    }
                    else
                    {
                        kmStr = "(n/d)";
                        mStr = "(n/d)";
                    }

                    list.Add(new PairDistanceDto
                    {
                        PersonA = a.name,
                        PersonB = b.name,
                        DistanceKm = kmStr,
                        DistanceMeters = mStr,
                        DistanceNumeric = (!double.IsNaN(d) && !double.IsInfinity(d)) ? d : double.NaN
                    });
                }
            }

            return list;
        }

        #endregion

        private void BtnRecalculate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_treeManager == null) return;
                _treeManager.GetEdgesWithWeights();
                _treeManager.ComputeAllDijkstras();
                _treeManager.ComputeMinMaxPairs();
                RefreshAll();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al recalcular: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            foreach (Window w in Application.Current.Windows)
            {
                if (w is MainWindow main)
                {
                    main.Show();
                    this.Close();
                    return;
                }
            }

            var newMain = new MainWindow();
            newMain.Show();
            this.Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (_treeManager != null)
                _treeManager.graphChanged -= TreeManager_graphChanged;
        }

        #region DTO
        private class PairDistanceDto
        {
            public string PersonA { get; set; } = "";
            public string PersonB { get; set; } = "";
            public string DistanceKm { get; set; } = "";
            public string DistanceMeters { get; set; } = "";
            // Distancia numérica para ordenar/filtrar internamente
            public double DistanceNumeric { get; set; } = double.NaN;
        }
        #endregion

        #region Utilities

        private void RecalculateGraphIfNeeded()
        {
            try { _treeManager?.GetEdgesWithWeights(); } catch { }
            try { _treeManager?.ComputeAllDijkstras(); } catch { }
            try { _treeManager?.ComputeMinMaxPairs(); } catch { }
        }

        #endregion
    }
}

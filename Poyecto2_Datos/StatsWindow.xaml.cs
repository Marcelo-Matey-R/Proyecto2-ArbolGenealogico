using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ArbolGenealogico.Core.Managers;
using ArbolGenealogico.Domain.Models;
using ArbolGenealogico.Domain.DTO;

namespace ProyectoDatos22
{
    public partial class StatsWindow : Window
    {
        private readonly TreeManager? _treeManager;

        // Constructor por defecto: intenta resolver TreeManager desde Application.Properties
        public StatsWindow() : this(ResolveTreeManagerFromApp()) { }

        // Constructor para inyección (tests / flexibilidad)
        public StatsWindow(TreeManager? treeManager)
        {
            InitializeComponent();

            _treeManager = treeManager;
            if (_treeManager != null)
                _treeManager.graphChanged += TreeManager_graphChanged;

            RefreshAll();
        }

        private static TreeManager? ResolveTreeManagerFromApp()
        {
            try
            {
                if (Application.Current?.Properties != null && Application.Current.Properties.Contains("TreeManager"))
                {
                    if (Application.Current.Properties["TreeManager"] is TreeManager tm) return tm;
                }

                // fallback: intentar crear uno (mantiene consistencia con AddNodeWindow)
                var newTm = Activator.CreateInstance<TreeManager>();
                if (Application.Current?.Properties != null && newTm is TreeManager ntm)
                {
                    Application.Current.Properties["TreeManager"] = ntm;
                    return ntm;
                }
            }
            catch { /* ignore and return null */ }
            return null;
        }

        private void TreeManager_graphChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(RefreshAll);
        }

        private void RefreshAll()
        {
            try
            {
                if (_treeManager == null)
                {
                    ShowTreeManagerUnavailable();
                    return;
                }

                EnsureGraphUpToDate();

                UpdateExtremePairDisplay();
                UpdateAverageDisplay();
                UpdatePairsGrid();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al refrescar estadísticas: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowTreeManagerUnavailable()
        {
            TxtFarthestPair.Text = "(TreeManager no disponible)";
            TxtClosestPair.Text = "(TreeManager no disponible)";
            TxtAverageDistance.Text = "(TreeManager no disponible)";
            TxtFarthestDistance.Text = "";
            TxtClosestDistance.Text = "";
            DgPairs.ItemsSource = null;
            LblCount.Text = "";
        }

        #region Extremes (closest / farthest)

        private void UpdateExtremePairDisplay()
        {
            UpdateFarthest();
            UpdateClosest();
        }

        private void UpdateFarthest()
        {
            var far = _treeManager!.personasMaxDistance;
            if (far.Item1 != null && far.Item2 != null)
            {
                TxtFarthestPair.Text = $"{far.Item1.name} ↔ {far.Item2.name}";
                double dFar = ObtainExtremeDistance(_treeManager.maxDistance, far.Item1, far.Item2);
                TxtFarthestDistance.Text = FormatDistanceDisplay(dFar);
            }
            else
            {
                TxtFarthestPair.Text = "(sin datos)";
                TxtFarthestDistance.Text = "";
            }
        }

        private void UpdateClosest()
        {
            var close = _treeManager!.personasMinDistance;
            if (close.Item1 != null && close.Item2 != null)
            {
                TxtClosestPair.Text = $"{close.Item1.name} ↔ {close.Item2.name}";
                double dClose = ObtainExtremeDistance(_treeManager.minDistance, close.Item1, close.Item2);
                TxtClosestDistance.Text = FormatDistanceDisplay(dClose);
            }
            else
            {
                TxtClosestPair.Text = "(sin datos)";
                TxtClosestDistance.Text = "";
            }
        }

        private double ObtainExtremeDistance(double? precomputedValue, Persona per1, Persona per2)
        {
            if (precomputedValue.HasValue) return precomputedValue.Value;
            return GetSafeDistance(per1, per2);
        }

        private string FormatDistanceDisplay(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "(sin datos)";
            return $"{d:F3} km ({KmToMeters(d):F0} m)";
        }

        #endregion

        #region Average

        private void UpdateAverageDisplay()
        {
            var avg = _treeManager!.averageDistances;
            if (!double.IsNaN(avg) && !double.IsInfinity(avg))
                TxtAverageDistance.Text = $"{avg:F3} km";
            else
                TxtAverageDistance.Text = "(sin datos)";
        }

        #endregion

        #region DataGrid: pares

        private void UpdatePairsGrid()
        {
            var pairs = BuildAllPairsList();
            // ordenar por DistanceNumeric descendente; NaN -> colocar al final
            var ordered = pairs.OrderByDescending(p => double.IsNaN(p.DistanceNumeric) ? double.NegativeInfinity : p.DistanceNumeric)
                               .ToList();
            DgPairs.ItemsSource = ordered;
            LblCount.Text = $"Pares listados: {pairs.Count}";
        }

        private List<PairDistanceDto> BuildAllPairsList()
        {
            var list = new List<PairDistanceDto>();
            if (_treeManager == null) return list;

            var nodes = CollectNodesForDistanceCalculation();
            var seen = new HashSet<(Guid, Guid)>();

            for (int i = 0; i < nodes.Count; i++)
            {
                for (int j = i + 1; j < nodes.Count; j++)
                {
                    var a = nodes[i].familiar;
                    var b = nodes[j].familiar;
                    if (a == null || b == null) continue;
                    if (a.excludeFromDistance || b.excludeFromDistance) continue;

                    var key = a.id.CompareTo(b.id) <= 0 ? (a.id, b.id) : (b.id, a.id);
                    if (seen.Contains(key)) continue;
                    seen.Add(key);

                    var dNumeric = GetSafeDistance(a, b); // km or NaN

                    var dto = new PairDistanceDto
                    {
                        PersonA = a.name,
                        PersonB = b.name,
                        DistanceNumeric = dNumeric
                    };

                    if (!double.IsNaN(dNumeric) && !double.IsInfinity(dNumeric))
                    {
                        dto.DistanceKm = dNumeric.ToString("F6");
                        dto.DistanceMeters = (dNumeric * 1000.0).ToString("F1");
                    }
                    else
                    {
                        dto.DistanceKm = "(n/d)";
                        dto.DistanceMeters = "(n/d)";
                    }

                    list.Add(dto);
                }
            }

            return list;
        }

        private List<Node> CollectNodesForDistanceCalculation()
        {
            if (_treeManager == null) return new List<Node>();

            var nodes = new List<Node>();
            foreach (var root in _treeManager.Roots)
            {
                root.TransverseDFS(n => nodes.Add(n));
            }

            return nodes
                    .Distinct()
                    .Where(n => n?.familiar != null && n.familiar.excludeFromDistance == false)
                    .ToList();
        }

        #endregion

        #region Distancia segura / utilitarios

        // Usa distancias precomputadas si es posible; si no, calcula con CalcDistance.
        private double GetDistanceBetween(Persona? a, Persona? b)
        {
            if (a == null || b == null) return double.NaN;

            // intentar usar distancias precomputadas en TreeManager (Dijkstra)
            try
            {
                var nodeA = _treeManager?.FindNodeById(a.id);
                var nodeB = _treeManager?.FindNodeById(b.id);
                if (nodeA != null && nodeB != null && nodeA.distances != null && nodeA.distances.ContainsKey(nodeB))
                {
                    return nodeA.distances[nodeB];
                }
            }
            catch { /* ignore and fallback to calc */ }

            // fallback: calcular geodésica
            try
            {
                var calc = new ArbolGenealogico.Infraestructure.Services.CalcDistance();
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

            return d; // may be NaN
        }

        private static double KmToMeters(double km) => km * 1000.0;

        #endregion

        #region Recalc / navigation / lifecycle

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
            // Buscar instancia abierta de MainWindow
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

        private void EnsureGraphUpToDate()
        {
            if (_treeManager == null) return;

            try { _treeManager.GetEdgesWithWeights(); } catch { }
            try { _treeManager.ComputeAllDijkstras(); } catch { }
            try { _treeManager.ComputeAverageShortestPathDistance(); } catch { }
            try { _treeManager.ComputeMinMaxPairs(); } catch { }
        }

        #endregion
    }
}

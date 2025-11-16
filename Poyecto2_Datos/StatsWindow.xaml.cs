using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ArbolGenealogico.Core.Managers;
using ArbolGenealogico.Domain.Models;

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

        private TreeManager? ResolveTreeManager()
        {
            if (Application.Current?.Properties != null && Application.Current.Properties.Contains("TreeManager"))
            {
                if (Application.Current.Properties["TreeManager"] is TreeManager tm) return tm;
            }

            // fallback: intentar crear uno (esto mantendrá consistencia con AddNodeWindow)
            try
            {
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

        private void TreeManager_graphChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() => RefreshAll());
        }

        private void RefreshAll()
        {
            try
            {
                if (_treeManager == null)
                {
                    TxtFarthestPair.Text = "(TreeManager no disponible)";
                    TxtClosestPair.Text = "(TreeManager no disponible)";
                    TxtAverageDistance.Text = "(TreeManager no disponible)";
                    DgPairs.ItemsSource = null;
                    LblCount.Text = "";
                    return;
                }

                // Asegurarnos de que grafo/dists estén actualizados
                // (TreeManager normalmente actualiza en AddPerson / ReassignParent)
                // pero permitimos recalcular para seguridad:
                try { _treeManager.GetEdgesWithWeights(); } catch { }
                try { _treeManager.ComputeAllDijkstras(); } catch { }
                try { _treeManager.ComputeMinMaxPairs(); } catch { }

                // 1) Par más lejano y distancia
                // 1) Par más lejano y distancia (usar GetSafeDistance para parejas)
                var far = _treeManager.personasMaxDistance;
                if (far.Item1 != null && far.Item2 != null)
                {
                    TxtFarthestPair.Text = $"{far.Item1.name} ↔ {far.Item2.name}";
                    var dFar = GetSafeDistance(far.Item1, far.Item2);
                    if (!double.IsNaN(dFar) && !double.IsInfinity(dFar))
                    {
                        TxtFarthestDistance.Text = $"{dFar:F3} km ({KmToMeters(dFar):F0} m)";
                    }
                    else
                    {
                        TxtFarthestDistance.Text = "(sin datos)";
                    }
                }
                else
                {
                    TxtFarthestPair.Text = "(sin datos)";
                    TxtFarthestDistance.Text = "";
                }

                // 2) Par más cercano y distancia (usar GetSafeDistance)
                var close = _treeManager.personasMinDistance;
                if (close.Item1 != null && close.Item2 != null)
                {
                    TxtClosestPair.Text = $"{close.Item1.name} ↔ {close.Item2.name}";
                    var dClose = GetSafeDistance(close.Item1, close.Item2);
                    if (!double.IsNaN(dClose) && !double.IsInfinity(dClose))
                    {
                        TxtClosestDistance.Text = $"{dClose:F3} km ({KmToMeters(dClose):F0} m)";
                    }
                    else
                    {
                        TxtClosestDistance.Text = "(sin datos)";
                    }
                }
                else
                {
                    TxtClosestPair.Text = "(sin datos)";
                    TxtClosestDistance.Text = "";
                }


                // 3) Distancia promedio (TreeManager.averageDistances)
                var avg = _treeManager.averageDistances;
                if (!double.IsNaN(avg) && !double.IsInfinity(avg))
                {
                    TxtAverageDistance.Text = $"{avg:F3} km";
                }
                else
                {
                    TxtAverageDistance.Text = "(sin datos)";
                }

                // 4) Llenar DataGrid con todas las parejas (unique unordered pairs)
                var pairs = BuildAllPairsList();
                DgPairs.ItemsSource = pairs.OrderByDescending(p => p.DistanceKm).ToList();
                LblCount.Text = $"Pares listados: {pairs.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al refrescar estadísticas: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Helper: obtiene distancia entre dos Personas usando la información existente en TreeManager (si ya la calculó),
        // sino intenta calcular directamente por sus coordenadas.
        private double GetDistanceBetween(Persona? a, Persona? b)
        {
            if (a == null || b == null) return double.NaN;

            // Intentar usar las distancias precomputadas (Dijkstra) si están presentes
            try
            {
                var nodeA = _treeManager?.FindNodeById(a.id);
                var nodeB = _treeManager?.FindNodeById(b.id);
                if (nodeA != null && nodeB != null && nodeA.distances != null && nodeA.distances.ContainsKey(nodeB))
                {
                    return nodeA.distances[nodeB];
                }
            }
            catch { /* ignorar y calcular directo */ }

            // fallback: calcular geodésica usando CalcDistance (devuelve km)
            try
            {
                var calc = new ArbolGenealogico.Infraestructure.Services.CalcDistance();
                var d = calc.Distance(a.lon, a.lat, b.lon, b.lat);
                return d;
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

            // Si no hay distancia válida, pero son pareja entre sí -> devolver 0
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

            return d; // puede ser NaN
        }

        private double KmToMeters(double km) => km * 1000.0;

        private List<PairDistance> BuildAllPairsList()
        {
            var list = new List<PairDistance>();
            if (_treeManager == null) return list;

            // Recolectar nodos
            var nodes = _treeManager.Roots.SelectMany(r =>
            {
                var temp = new List<Node>();
                r.TransverseDFS(n => temp.Add(n));
                return temp;
            }).Distinct().ToList();

            // Uso de diccionario para evitar duplicados (pairs unordered)
            var seen = new HashSet<(Guid, Guid)>();

            for (int i = 0; i < nodes.Count; i++)
            {
                for (int j = i + 1; j < nodes.Count; j++)
                {
                    var a = nodes[i].familiar;
                    var b = nodes[j].familiar;
                    if (a == null || b == null) continue;

                    var key = a.id.CompareTo(b.id) <= 0 ? (a.id, b.id) : (b.id, a.id);
                    if (seen.Contains(key)) continue;
                    seen.Add(key);

                    // Usar GetSafeDistance que devuelve 0 para parejas sin distancia válida
                    var d = GetSafeDistance(a, b);

                    string kmStr;
                    string mStr;
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

                    var entry = new PairDistance
                    {
                        PersonA = a.name,
                        PersonB = b.name,
                        DistanceKm = kmStr,
                        DistanceMeters = mStr
                    };
                    list.Add(entry);
                }
            }

            return list;
        }


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

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var pairs = BuildAllPairsList();
                if (pairs == null || pairs.Count == 0)
                {
                    MessageBox.Show("No hay pares para exportar.", "Exportar CSV", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dlg = new SaveFileDialog()
                {
                    Title = "Exportar pares a CSV",
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    FileName = "pares_distancias.csv"
                };
                if (dlg.ShowDialog() != true) return;

                using (var sw = new StreamWriter(dlg.FileName))
                {
                    sw.WriteLine("PersonA,PersonB,Distance_km,Distance_m");
                    foreach (var p in pairs)
                    {
                        // Escape comas en nombres si es necesario
                        var a = $"\"{p.PersonA.Replace("\"", "\"\"")}\"";
                        var b = $"\"{p.PersonB.Replace("\"", "\"\"")}\"";
                        var km = p.DistanceKm;
                        var m = p.DistanceMeters;
                        sw.WriteLine($"{a},{b},{km},{m}");
                    }
                }

                MessageBox.Show("Exportado correctamente.", "Exportar CSV", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al exportar CSV: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            // Buscar si hay una instancia abierta del MainWindow
            foreach (Window w in Application.Current.Windows)
            {
                if (w is MainWindow main)
                {
                    main.Show();
                    this.Close();
                    return;
                }
            }

            // Si no está abierta, crear una nueva instancia
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

        // DTO para datagrid
        private class PairDistance
        {
            public string PersonA { get; set; } = "";
            public string PersonB { get; set; } = "";
            public string DistanceKm { get; set; } = "";
            public string DistanceMeters { get; set; } = "";
        }
    }
}

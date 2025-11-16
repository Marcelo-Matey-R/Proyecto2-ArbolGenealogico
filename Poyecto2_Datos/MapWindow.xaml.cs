using ArbolGenealogico.Core.Managers;
using ArbolGenealogico.Domain.Models;
using ExCSS;
using Mapsui;                         // MPoint, MRect, Map, ...
using Mapsui.Features;
using Mapsui.Layers;                  // MemoryLayer, PointFeature
using Mapsui.Manipulations;// MapControl
using Mapsui.Providers;               // MemoryProvider
using Mapsui.Styles;                  // SymbolStyle, Brush, Pen
using Mapsui.UI.Wpf;
using NetTopologySuite.Geometries;
using Mapsui.Nts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Drawing;
using static Mapsui.Tiling.OpenStreetMap; // CreateTileLayer()

namespace Poyecto2_Datos
{
    public partial class MapWindow : Window
    {
        private readonly TreeManager? _treeManager;
        private readonly List<IFeature> _features = new();
        private MemoryLayer? _distanceLayer = null;     // capa para líneas + etiquetas
        private const string DistanceLayerName = "DistanceLines";
        /// <summary>
        /// Carga un System.Drawing.Bitmap desde archivo de forma segura (sin dejar el archivo bloqueado)
        /// y devuelve una copia redimensionada a (width x height). El objeto devuelto debe ser dispuesto por el caller.
        /// </summary>
        private System.Drawing.Bitmap? LoadBitmapFromFileSafeAndResize(string path, int width, int height)
        {
            try
            {
                using (var src = System.Drawing.Image.FromFile(path))
                {
                    // calcular tamaño manteniendo aspecto
                    int targetW = width;
                    int targetH = height;

                    var bmp = new System.Drawing.Bitmap(targetW, targetH);
                    using (var g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        g.Clear(System.Drawing.Color.Transparent);
                        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                        // mantener aspect ratio centrado
                        double ratioSrc = (double)src.Width / src.Height;
                        double ratioTgt = (double)targetW / targetH;

                        int drawW = targetW, drawH = targetH;
                        if (ratioSrc > ratioTgt)
                        {
                            // source más ancho -> ajustar por width
                            drawW = targetW;
                            drawH = (int)Math.Round(targetW / ratioSrc);
                        }
                        else
                        {
                            drawH = targetH;
                            drawW = (int)Math.Round(targetH * ratioSrc);
                        }

                        int offX = (targetW - drawW) / 2;
                        int offY = (targetH - drawH) / 2;

                        g.DrawImage(src, offX, offY, drawW, drawH);
                    }

                    return bmp; // caller debe disponer
                }
            }
            catch
            {
                return null;
            }
        }


        public MapWindow()
        {
            InitializeComponent();
            _treeManager = ResolveTreeManager();
            InitializeMap();
            RefreshMarkers();
            // Suscribirse a cambios si quieres que se refresque automáticamente:
            if (_treeManager != null) _treeManager.graphChanged += (_, __) => Dispatcher.Invoke(RefreshMarkers);
        }

        private TreeManager? ResolveTreeManager()
        {
            if (Application.Current?.Properties != null && Application.Current.Properties.Contains("TreeManager"))
            {
                if (Application.Current.Properties["TreeManager"] is TreeManager existing) return existing;
            }

            // fallback: crear uno nuevo y guardarlo
            try
            {
                var newTm = Activator.CreateInstance<TreeManager>();
                if (Application.Current?.Properties != null)
                    Application.Current.Properties["TreeManager"] = newTm;
                return newTm;
            }
            catch
            {
                return null;
            }
        }

        private void InitializeMap()
        {
            var map = new Map();

            // capa base OSM
            map.Layers.Add(CreateTileLayer());

            // Capa de marcadores (MemoryLayer). Usaremos Features para asignar la lista.
            var markerLayer = new MemoryLayer
            {
                Name = "PersonMarkers",
                Features = new List<IFeature>() // inicial vacío
            };
            map.Layers.Add(markerLayer);
            // después de añadir markerLayer
            _distanceLayer = new MemoryLayer
            {
                Name = DistanceLayerName,
                Features = new List<IFeature>()
            };
            map.Layers.Add(_distanceLayer);

            // Asignar mapa al control
            mapControl.Map = map;

            // Manejar clicks: GetMapInfo espera un MPoint en DIP (usaremos ToDeviceIndependentUnits)
            mapControl.MouseLeftButtonUp += MapControl_MouseLeftButtonUp;
        }

        private void RefreshMarkers()
        {
            try
            {
                _features.Clear();
                if (_treeManager == null || mapControl?.Map == null) return;

                // Recolectar todos los nodos (sin duplicados)
                var allNodes = new List<Node>();
                foreach (var root in _treeManager.Roots)
                    root.TransverseDFS(n => { if (n != null && !allNodes.Contains(n)) allNodes.Add(n); });

                // Obtener o crear la layer de marcadores
                var layer = mapControl.Map.Layers.FirstOrDefault(l => l.Name == "PersonMarkers") as MemoryLayer;
                if (layer == null)
                {
                    layer = new MemoryLayer
                    {
                        Name = "PersonMarkers",
                        Features = new List<IFeature>(),
                        Style = null
                    };
                    mapControl.Map.Layers.Add(layer);
                }

                var newFeatures = new List<IFeature>();

                foreach (var node in allNodes)
                {
                    var person = node?.familiar;
                    if (person == null) continue;

                    // Intentar rellenar coords desde pluscode si faltan
                    if (!person.HasCoordinates() && !string.IsNullOrWhiteSpace(person.addresPlusCode))
                    {
                        try
                        {
                            var calc = new ArbolGenealogico.Infraestructure.Services.CalcDistance();
                            if (calc.TryConvertPlusCode(person.addresPlusCode, out double lon, out double lat))
                            {
                                person.lon = lon;
                                person.lat = lat;
                            }
                        }
                        catch { /* ignore */ }
                    }

                    if (!person.lon.HasValue || !person.lat.HasValue) continue;

                    // Proyectar lon/lat a WebMercator
                    (double x, double y) = Mapsui.Projections.SphericalMercator.FromLonLat(person.lon.Value, person.lat.Value);
                    var mp = new MPoint(x, y);

                    var feat = new PointFeature(mp);

                    // Construir símbolo base (sin SymbolType.Image, para evitar error)
                    var symbol = new Mapsui.Styles.SymbolStyle
                    {
                        // NO ASIGNAR SymbolType.Image aquí (da error en tu versión)
                        SymbolScale = 1.0,
                        RotateWithMap = false
                    };

                    bool symbolAssigned = false;
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(person.photoFileName) && File.Exists(person.photoFileName))
                        {
                            string filePath = person.photoFileName;
                            string fileUri = new Uri(filePath).AbsoluteUri; // file:///...

                            // 1) Intentar IconPath (Mapsui v5+)
                            var iconPathProp = typeof(Mapsui.Styles.SymbolStyle).GetProperty("IconPath");
                            if (iconPathProp != null && iconPathProp.CanWrite && iconPathProp.PropertyType == typeof(string))
                            {
                                iconPathProp.SetValue(symbol, fileUri);
                                symbolAssigned = true;
                            }

                            // 2) Si no se asignó, intentar Image/ImageSource/IconName etc. (por reflexión)
                            if (!symbolAssigned)
                            {
                                var candidateProps = new[] { "Image", "ImageSource", "Icon", "IconSource" };
                                foreach (var name in candidateProps)
                                {
                                    var p = typeof(Mapsui.Styles.SymbolStyle).GetProperty(name);
                                    if (p != null && p.CanWrite)
                                    {
                                        if (p.PropertyType == typeof(string))
                                        {
                                            p.SetValue(symbol, fileUri);
                                            symbolAssigned = true;
                                            break;
                                        }
                                        else
                                        {
                                            try { p.SetValue(symbol, fileUri); symbolAssigned = true; break; } catch { }
                                        }
                                    }
                                }
                            }

                            // 3) Fallback: buscar BitmapRegistry/BitmapManager y usar Register por reflexión para obtener id
                            if (!symbolAssigned)
                            {
                                Type? registryType = null;
                                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                                {
                                    registryType = asm.GetType("Mapsui.Styles.BitmapRegistry") ?? asm.GetType("Mapsui.BitmapRegistry");
                                    if (registryType != null) break;
                                }

                                if (registryType != null)
                                {
                                    // buscar método Register que acepte string o Stream
                                    var regMethod = registryType.GetMethod("Register", new[] { typeof(string) })
                                                    ?? registryType.GetMethod("Register", new[] { typeof(Stream) })
                                                    ?? registryType.GetMethod("RegisterImage", new[] { typeof(string) });

                                    if (regMethod != null)
                                    {
                                        object? registryInstance = null;
                                        if (!regMethod.IsStatic)
                                        {
                                            var prop = registryType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                                            if (prop != null) registryInstance = prop.GetValue(null);
                                        }

                                        object? regResult = null;
                                        try
                                        {
                                            if (regMethod.GetParameters().Length == 1 && regMethod.GetParameters()[0].ParameterType == typeof(string))
                                            {
                                                regResult = regMethod.Invoke(registryInstance, new object[] { filePath });
                                            }
                                            else if (regMethod.GetParameters().Length == 1 && regMethod.GetParameters()[0].ParameterType == typeof(Stream))
                                            {
                                                using var fs = File.OpenRead(filePath);
                                                regResult = regMethod.Invoke(registryInstance, new object[] { fs });
                                            }
                                        }
                                        catch { regResult = null; }

                                        if (regResult is int bmpId)
                                        {
                                            var bmpIdProp = typeof(Mapsui.Styles.SymbolStyle).GetProperty("BitmapId");
                                            if (bmpIdProp != null && bmpIdProp.CanWrite)
                                            {
                                                bmpIdProp.SetValue(symbol, bmpId);
                                                symbolAssigned = true;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception exImg)
                    {
                        Console.WriteLine("Warning: fallo asignando imagen al símbolo: " + exImg.Message);
                    }

                    // Si aún no se asignó una imagen válida, usar un símbolo simple (círculo/pin)
                    if (!symbolAssigned)
                    {
                        var fill = new Mapsui.Styles.Brush(new Mapsui.Styles.Color(0, 140, 200, 220));
                        var outline = new Mapsui.Styles.Pen(new Mapsui.Styles.Color(255, 255, 255, 200), 1);
                        symbol = new Mapsui.Styles.SymbolStyle
                        {
                            SymbolType = Mapsui.Styles.SymbolType.Ellipse,
                            SymbolScale = 0.8,
                            Fill = fill,
                            Outline = outline
                        };
                    }

                    // añadir estilo y atributos
                    feat.Styles.Add(symbol);
                    feat["personaId"] = person.id.ToString();
                    feat["name"] = person.name ?? "";

                    newFeatures.Add(feat);
                }

                // Asignar features a la capa y refrescar mapa
                layer.Features = newFeatures;
                mapControl.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al refrescar marcadores: " + ex.Message, "Mapa", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void EnsureDistanceLayer()
        {
            if (mapControl?.Map == null) return;
            if (_distanceLayer == null)
            {
                _distanceLayer = mapControl.Map.Layers.FirstOrDefault(l => l.Name == DistanceLayerName) as MemoryLayer;
                if (_distanceLayer == null)
                {
                    _distanceLayer = new MemoryLayer
                    {
                        Name = DistanceLayerName,
                        Features = new List<IFeature>(),
                        // evitar que el estilo por defecto dibuje puntos blancos grandes debajo
                        Style = null
                    };
                    mapControl.Map.Layers.Add(_distanceLayer);
                }
            }
        }
        private void BringDistanceLayerToFront()
        {
            if (mapControl?.Map == null || _distanceLayer == null) return;

            var layers = mapControl.Map.Layers.ToList();

            // Si ya existe, la movemos al final (para que se dibuje encima de las demás)
            if (layers.Contains(_distanceLayer))
            {
                layers.Remove(_distanceLayer);
            }

            layers.Add(_distanceLayer);

            // Reasignamos el orden de capas al mapa
            mapControl.Map.Layers.Clear();
            foreach (var l in layers)
                mapControl.Map.Layers.Add(l);

            // Forzamos refresco visual
            mapControl.Refresh();
        }
        private void DrawDistanceLines(Guid selectedPersonId)
        {
            try
            {
                if (_treeManager == null || mapControl?.Map == null)
                {
                    MessageBox.Show("TreeManager o mapa nulo.", "Debug", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                EnsureDistanceLayer();
                if (_distanceLayer == null)
                {
                    MessageBox.Show("No se pudo obtener o crear la capa de distancias.", "Debug", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // limpiar capa
                _distanceLayer.Features = new List<IFeature>();

                var selNode = _treeManager.FindNodeById(selectedPersonId);
                if (selNode == null)
                {
                    MessageBox.Show($"Nodo con id {selectedPersonId} no encontrado en TreeManager.", "Debug", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Si no hay distancias, avisar
                if (selNode.distances == null || !selNode.distances.Any())
                {
                    MessageBox.Show($"El nodo {selNode.familiar?.name ?? selectedPersonId.ToString()} no tiene distancias calculadas (distances.Count == 0). Asegúrate de llamar ComputeAllDijkstras().", "Debug", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var selPerson = selNode.familiar;
                if (selPerson == null)
                {
                    MessageBox.Show("Persona seleccionada nula.", "Debug", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // intentar coords del seleccionado
                if (!selPerson.HasCoordinates() && !string.IsNullOrWhiteSpace(selPerson.addresPlusCode))
                {
                    var calc = new ArbolGenealogico.Infraestructure.Services.CalcDistance();
                    if (calc.TryConvertPlusCode(selPerson.addresPlusCode, out double lon0, out double lat0))
                    {
                        selPerson.lon = lon0; selPerson.lat = lat0;
                    }
                }

                if (!selPerson.HasCoordinates())
                {
                    MessageBox.Show($"La persona seleccionada '{selPerson.name}' no tiene coordenadas válidas.", "Debug", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // proyectar seleccionado
                (double sx, double sy) = Mapsui.Projections.SphericalMercator.FromLonLat(selPerson.lon.Value, selPerson.lat.Value);

                // contador de líneas añadidas
                int added = 0;

                var newFeatures = new List<IFeature>();
                var geomFactory = new NetTopologySuite.Geometries.GeometryFactory();
                foreach (var kv in selNode.distances)
                {
                    var targetNode = kv.Key;
                    var distKm = kv.Value;
                    if (double.IsNaN(distKm) || double.IsInfinity(distKm)) continue;
                    var tp = targetNode.familiar;
                    if (tp == null) continue;

                    // intentar coords del target
                    if (!tp.HasCoordinates() && !string.IsNullOrWhiteSpace(tp.addresPlusCode))
                    {
                        var calc = new ArbolGenealogico.Infraestructure.Services.CalcDistance();
                        if (calc.TryConvertPlusCode(tp.addresPlusCode, out double lon2, out double lat2))
                        {
                            tp.lon = lon2; tp.lat = lat2;
                        }
                    }
                    if (!tp.HasCoordinates()) continue;

                    // proyectar target
                    (double tx, double ty) = Mapsui.Projections.SphericalMercator.FromLonLat(tp.lon.Value, tp.lat.Value);

                    // crear coords NTS y linea
                    var coords = new NetTopologySuite.Geometries.Coordinate[] {
        new NetTopologySuite.Geometries.Coordinate(sx, sy),
        new NetTopologySuite.Geometries.Coordinate(tx, ty)
    };
                    var ntsLine = geomFactory.CreateLineString(coords);
                    var lineFeature = new Mapsui.Nts.GeometryFeature { Geometry = ntsLine };

                    // estilo
                    var vstyle = new Mapsui.Styles.VectorStyle
                    {
                        Line = new Mapsui.Styles.Pen(new Mapsui.Styles.Color(220, 20, 20, 255), 4f),
                        Outline = new Mapsui.Styles.Pen(new Mapsui.Styles.Color(255, 255, 255, 200), 1f)
                    };
                    lineFeature.Styles.Add(vstyle);
                    lineFeature["distanceKm"] = distKm;
                    newFeatures.Add(lineFeature);
                    lineFeature["targetName"] = tp.name ?? "";

                    (_distanceLayer.Features as List<IFeature>)?.Add(lineFeature);

                    // label en medio
                    // --- crear etiqueta en el punto medio (reemplaza la sección anterior) ---
                    var midX = (sx + tx) / 2.0;
                    var midY = (sy + ty) / 2.0;
                    var midPoint = new Mapsui.Layers.PointFeature(new Mapsui.MPoint(midX, midY));

                    // Guardamos la etiqueta como atributo (LabelColumn -> "Label")
                    midPoint["Label"] = $"{distKm:F2} km";

                    // Crear LabelStyle que toma el texto desde la columna "Label"
                    var labelStyle = new Mapsui.Styles.LabelStyle
                    {
                        // Indicar que el texto viene de la propiedad "Label" de la feature
                        LabelColumn = "Label",

                        // Opciones de visibilidad / tamaño
                        Font = new Mapsui.Styles.Font { Size = 14 }, // aumentar si lo ves pequeño
                        ForeColor = new Mapsui.Styles.Color(0, 0, 0, 255),

                        // Halo → usar Pen (contorno blanco) para contraste
                        Halo = new Mapsui.Styles.Pen(new Mapsui.Styles.Color(255, 255, 255, 220), 2f),

                        // Offset para que la etiqueta no se sobreponga exactamente al símbolo
                        Offset = new Mapsui.Styles.Offset(0, -12),

                        // Forzar dibujo aunque existan solapamientos
                        CollisionDetection = false,

                        // Asegurar que siempre sea visible (rango amplio)
                        MinVisible = double.MinValue,
                        MaxVisible = double.MaxValue
                    };

                    // Añadir estilos al feature (primero label)
                    midPoint.Styles.Add(labelStyle);

                    // Añadir un fondo/símbolo pequeño para la etiqueta (mejora legibilidad)
                    midPoint.Styles.Add(new Mapsui.Styles.SymbolStyle
                    {
                        SymbolType = Mapsui.Styles.SymbolType.Ellipse,
                        Fill = new Mapsui.Styles.Brush(new Mapsui.Styles.Color(255, 255, 255, 220)),
                        Outline = new Mapsui.Styles.Pen(new Mapsui.Styles.Color(120, 120, 120, 150), 1f),
                        SymbolScale = 0.45
                    });

                    // Finalmente agregar midPoint a la lista de features del layer
                    newFeatures.Add(midPoint);

                    _distanceLayer.Features = newFeatures;
                    mapControl.Refresh();
                    BringDistanceLayerToFront();
                }

                // refrescar
                mapControl.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error en DrawDistanceLines: " + ex.Message, "Debug", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MapControl_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // 1) convertir la posición del ratón a ScreenPosition
                var pos = e.GetPosition(mapControl);
                var screenPos = new ScreenPosition((int)pos.X, (int)pos.Y);

                // 2) llamar a la versión síncrona GetMapInfo (pasa las capas del mapa)
                var layers = mapControl.Map?.Layers ?? Enumerable.Empty<ILayer>();
                var mapInfo = mapControl.GetMapInfo(screenPos, layers);

                if (mapInfo?.Feature != null)
                {

                    var feature = mapInfo.Feature;

                    // 3) intentar obtener atributo "personaId" o "name" de forma robusta
                    object? personaIdObj = TryGetFeatureAttribute(feature, "personaId")
                                           ?? TryGetFeatureAttribute(feature, "name");

                    if (personaIdObj != null)
                    {
                        // si es GUID -> abrir edición; sino mostrar texto
                        if (Guid.TryParse(personaIdObj.ToString(), out var pid))
                        {
                            // si parseaste pid con éxito:
                            DrawDistanceLines(pid);
                            var node = _treeManager?.FindNodeById(pid);
                            if (node != null)
                            {
                                MessageBox.Show($"Seleccionado: {node.familiar.name}\nOwnId: {node.familiar.ownId}", "Persona", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                            else
                            {
                                MessageBox.Show($"No se encontró nodo con id {pid}", "Mapa", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }
                        else
                        {
                            MessageBox.Show($"Info: {personaIdObj}", "Mapa", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // no romper UI por un fallo de hit-testing
                Console.WriteLine("Map click error: " + ex.Message);
            }
        }

        // Helper robusto que intenta leer atributos de una IFeature por varias estrategias
        private object? TryGetFeatureAttribute(IFeature feature, string key)
        {
            if (feature == null) return null;

            try
            {
                // 1) intentar indexador (feature[key]) si existe (algunas impls lo permiten)
                try
                {
                    // usar dynamic para intentar indexador si está presente
                    dynamic d = feature;
                    try
                    {
                        var val = d[key];
                        if (val != null) return val;
                    }
                    catch { /* no tiene indexador */ }
                }
                catch { /* sigh */ }

                // 2) intentar propiedad 'Attributes' (podría ser IDictionary)
                var prop = feature.GetType().GetProperty("Attributes", BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                {
                    var attrs = prop.GetValue(feature);
                    if (attrs is System.Collections.IDictionary dict && dict.Contains(key))
                        return dict[key];
                }

                // 3) intentar método TryGetValue(string, out object) por reflexión
                var tryGet = feature.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                     .FirstOrDefault(m => m.Name.Equals("TryGetValue", StringComparison.OrdinalIgnoreCase)
                                                          && m.GetParameters().Length == 2);
                if (tryGet != null)
                {
                    var parameters = new object?[] { key, null };
                    var ok = (bool)tryGet.Invoke(feature, parameters)!;
                    if (ok) return parameters[1];
                }

                // 4) intentar Properties o similar (fallback)
                var prop2 = feature.GetType().GetProperty(key, BindingFlags.Public | BindingFlags.Instance);
                if (prop2 != null)
                {
                    var v = prop2.GetValue(feature);
                    if (v != null) return v;
                }
            }
            catch
            {
                // ignorar errores de reflexión y seguir
            }

            return null;
        }
        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            // Buscar si ya existe el menú principal abierto
            foreach (Window win in Application.Current.Windows)
            {
                if (win is MainWindow main)
                {
                    main.Show();
                    this.Close();
                    return;
                }
            }

            // Si no existe, crear una nueva instancia
            var newMain = new MainWindow();
            newMain.Show();
            this.Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (_treeManager != null) _treeManager.graphChanged -= (_, __) => RefreshMarkers();
        }
    }
}

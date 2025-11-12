using Microsoft.Win32;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ArbolGenealogico.Core.Managers;
using ArbolGenealogico.Domain.Models;
using ArbolGenealogico.Infraestructure.Services;

namespace Poyecto2_Datos
{
    public partial class AddNodeWindow : Window, IDisposable
    {
        private string? _photoFilePath = null;
        private double? _lat = null;
        private double? _lon = null;
        private TreeManager? _treeManager;
        // CACHE de imágenes para evitar reloads y bloqueo de archivos
        private readonly Dictionary<string, BitmapImage> _imageCache = new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);


        // Nodo que estamos editando (null = modo añadir)
        private Node? _editingNode = null;

        public AddNodeWindow()
        {
            InitializeComponent();

            _treeManager = ResolveTreeManager();
            SubscribeToTreeManager();

            LoadParentsCombo();
            UpdateCanvasLayout();
            LoadPartnersCombo();
        }

        #region Resolve TreeManager and subscription
        // busca si ya existe una instancia de treemanager y se suscribe a los cambios del arbol
        private TreeManager? ResolveTreeManager()
        {
            if (Application.Current?.Properties != null && Application.Current.Properties.Contains("TreeManager"))
            {
                if (Application.Current.Properties["TreeManager"] is TreeManager tm) return tm;
            }

            var tmType = typeof(TreeManager);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    try
                    {
                        var props = t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        foreach (var p in props)
                        {
                            if (tmType.IsAssignableFrom(p.PropertyType))
                            {
                                var val = p.GetValue(null);
                                if (val is TreeManager found) return found;
                            }
                        }

                        var fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        foreach (var f in fields)
                        {
                            if (tmType.IsAssignableFrom(f.FieldType))
                            {
                                var val = f.GetValue(null);
                                if (val is TreeManager found) return found;
                            }
                        }
                    }
                    catch { }
                }
            }

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

        private void SubscribeToTreeManager()
        {
            if (_treeManager == null) return;
            _treeManager.graphChanged += TreeManager_graphChanged;
        }

        private void UnsubscribeFromTreeManager()
        {
            if (_treeManager == null) return;
            _treeManager.graphChanged -= TreeManager_graphChanged;
        }

        private void TreeManager_graphChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                LoadParentsCombo();
                LoadPartnersCombo();
                UpdateCanvasLayout();
            });
        }
        #endregion

        #region UI helpers (combo population etc.)
        // carga el combobox de la interfaz con los nodos excepto el nodo seleccionado
        private void LoadParentsCombo()
        {
            try
            {
                CmbParent.Items.Clear();

                var placeholderPersona = new Persona(Guid.Empty, "(No asignado)", 0, DateTime.MinValue, "", "", null, null, null, null, false);
                var placeholderNode = new Node(placeholderPersona);
                CmbParent.Items.Add(placeholderNode);

                if (_treeManager == null)
                {
                    CmbParent.SelectedIndex = 0;
                    return;
                }

                var roots = _treeManager.Roots;
                foreach (var r in roots)
                    AddNodeAndChildrenToCombo(r);

                CmbParent.SelectedIndex = 0;
            }
            catch
            {
            }
        }
        // carga el combobox de pareja con todos los nodos excepto el del nodo seleccionado
        private void LoadPartnersCombo(Guid? excludeId = null)
        {
            try
            {
                // usar ItemsSource es más fiable que Items.Add repetido
                var items = new List<Node>();

                // placeholder "Ninguna"
                var placeholderPersona = new Persona(Guid.Empty, "(Ninguna)", 0, DateTime.MinValue, "", "", null, null, null, null, false);
                var placeholderNode = new Node(placeholderPersona);
                items.Add(placeholderNode);

                if (_treeManager != null)
                {
                    // recolectar todos los nodos a partir de las raíces
                    var roots = _treeManager.Roots;
                    var allNodes = new List<Node>();
                    foreach (var r in roots)
                    {
                        r.TransverseDFS(n =>
                        {
                            if (n != null && !allNodes.Contains(n))
                                allNodes.Add(n);
                        });
                    }

                    // añade solo nodos válidos (evitar agregar placeholder de nuevo)
                    foreach (var n in allNodes)
                    {
                        // opcional: filtrar nodos con id == Guid.Empty por seguridad
                        if (n.familiar == null || n.familiar.id == Guid.Empty) continue;

                        // excluir el nodo que estamos editando (si se proporcionó)
                        if (excludeId.HasValue && n.familiar.id == excludeId.Value) continue;

                        items.Add(n);
                    }
                }

                // asignar ItemsSource y DisplayMember (asegurarnos que DisplayMemberPath coincida)
                CmbPartnerSelect.DisplayMemberPath = "familiar.name"; // muestra Persona.name
                CmbPartnerSelect.SelectedValuePath = "familiar.id";
                CmbPartnerSelect.ItemsSource = items;

                // seleccionar placeholder por defecto
                CmbPartnerSelect.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                // si falla, al menos no rompe la UI
                Console.WriteLine("LoadPartnersCombo error: " + ex.Message);
                CmbPartnerSelect.ItemsSource = new List<Node> { new Node(new Persona(Guid.Empty, "(Ninguna)", 0, DateTime.MinValue)) };
                CmbPartnerSelect.SelectedIndex = 0;
            }
        }


        private void AddNodeAndChildrenToCombo(Node node)
        {
            if (node == null) return;
            CmbParent.Items.Add(node);
            foreach (var c in node.children) AddNodeAndChildrenToCombo(c);
        }
        #endregion

        #region Photo and pluscode controls
        //abre una ventana de archivos para buscar el filepath de una foto
        private void BtnLoadPhoto_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.bmp";
            if (dlg.ShowDialog() == true)
            {
                _photoFilePath = dlg.FileName;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(_photoFilePath);
                bmp.EndInit();
                ImgPhoto.Source = bmp;
            }
        }

        private void BtnClearPhoto_Click(object sender, RoutedEventArgs e)
        {
            _photoFilePath = null;
            ImgPhoto.Source = null;
        }
        // convierte el pluscode a lat y lon utilizando la clase CalcDistance
        private void BtnConvertPlusCode_Click(object sender, RoutedEventArgs e)
        {
            var code = (TxtPlusCode.Text ?? "").Trim();
            if (string.IsNullOrEmpty(code))
            {
                MessageBox.Show("Ingrese un Plus Code primero.", "Plus Code", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var calc = new CalcDistance();
            if (calc.TryConvertPlusCode(code, out double Lon, out double Lat))
            {
                _lat = Lat; _lon = Lon;
                LblLat.Text = Lat.ToString("F6");
                LblLon.Text = Lon.ToString("F6");
            }
            else
            {
                MessageBox.Show("No se pudo convertir el Plus Code. Revisa el formato.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion

        #region Save / Cancel
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            // Si estamos en edición, cancelar edición sino cerrar ventana
            if (_editingNode != null)
            {
                // cancelar edición y limpiar formulario
                _editingNode = null;
                BtnSave_SetAddMode();
                ClearFormFields();
            }
            else
            {
                this.Close();
            }
        }
        // este se encarga de aniadir el nodo al arbol
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var nombre = TxtNombre.Text?.Trim() ?? "";
            var ownId = TxtCedula.Text?.Trim() ?? "";
            var birth = DpFechaNacimiento.SelectedDate ?? DateTime.MinValue;
            var notes = TxtNotes.Text?.Trim() ?? "";
            var plus = TxtPlusCode.Text?.Trim() ?? "";
            // --- VALIDACIONES ---
            if (string.IsNullOrWhiteSpace(nombre) || nombre.Any(char.IsDigit))
            {
                MessageBox.Show("Nombre inválido. El campo nombre no puede contener números.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // ownId debe ser sólo dígitos (ajusta si deseas permitir letras)
            if (string.IsNullOrWhiteSpace(ownId) || !ownId.All(char.IsDigit))
            {
                MessageBox.Show("OwnId inválido. El campo Cédula debe contener sólo números.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(nombre))
            {
                MessageBox.Show("Nombre requerido.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(ownId))
            {
                MessageBox.Show("Cédula / ownId requerido.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int age = 0;
            if (birth != DateTime.MinValue)
            {
                var today = DateTime.Today;
                age = today.Year - birth.Year;
                if (birth.Date > today.AddYears(-age)) age--;
                if (age < 0) age = 0;
            }

            try
            {
                if (_editingNode == null)
                {
                    // MODO AÑADIR
                    Persona persona = new Persona(Guid.NewGuid(), nombre, age, birth, _photoFilePath ?? "", plus, _lon, _lat, null, null, false)
                    {
                        ownId = ownId
                    };

                    if ((!persona.HasCoordinates() || persona.lat == null || persona.lon == null) && !string.IsNullOrWhiteSpace(plus))
                    {
                        var calc = new CalcDistance();
                        if (calc.TryConvertPlusCode(plus, out double Lon, out double Lat))
                        {
                            persona.lon = Lon;
                            persona.lat = Lat;
                        }
                    }

                    Guid? parentId = null;
                    if (CmbParent.SelectedItem is Node selNode)
                    {
                        if (selNode.familiar != null && selNode.familiar.id != Guid.Empty)
                            parentId = selNode.familiar.id;
                    }

                    if (_treeManager == null)
                    {
                        MessageBox.Show("No se pudo resolver TreeManager.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    _treeManager.AddPerson(persona, parentId);
                    // --- después de agregar la persona al TreeManager ---
                    try
                    {
                        // obtener el id del partner seleccionado (si cualquiera)
                        Guid? selectedPartnerId = null;
                        if (CmbPartnerSelect.SelectedItem is Node selPartnerNode)
                        {
                            if (selPartnerNode.familiar != null && selPartnerNode.familiar.id != Guid.Empty)
                                selectedPartnerId = selPartnerNode.familiar.id;
                        }

                        if (selectedPartnerId.HasValue)
                        {
                            // evitar asociar la persona con sí misma
                            if (selectedPartnerId.Value == persona.id)
                            {
                                MessageBox.Show("No puedes asignar la misma persona como pareja.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                            else
                            {
                                try
                                {
                                    // SetPartner espera idA, idB -> aquí hacemos A = persona.id, B = selectedPartnerId
                                    _treeManager.SetPartner(persona.id, selectedPartnerId);
                                }
                                catch (Exception exPartner)
                                {
                                    MessageBox.Show("No se pudo asignar la pareja: " + exPartner.Message, "Pareja", MessageBoxButton.OK, MessageBoxImage.Warning);
                                }
                            }
                        }
                    }
                    catch
                    {
                        // ignorar errores no críticos
                    }

                    MessageBox.Show("Persona agregada correctamente.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);

                    LoadParentsCombo();
                    LoadPartnersCombo();
                    UpdateCanvasLayout();
                    ClearFormFields();
                }
                else
                {
                    // MODO EDITAR: actualizamos la persona existente en el nodo
                    var node = _editingNode;
                    var persona = node.familiar;

                    persona.name = nombre;
                    persona.ownId = ownId;
                    persona.birthdate = birth;
                    persona.age = age;
                    persona.photoFileName = _photoFilePath ?? persona.photoFileName;
                    persona.addresPlusCode = plus;
                    persona.lon = _lat.HasValue ? _lon : persona.lon;
                    persona.lat = _lat.HasValue ? _lat : persona.lat;

                    // Si el usuario escogió un padre distinto -> ReassignParent
                    Guid? newParentId = null;
                    if (CmbParent.SelectedItem is Node selNode)
                    {
                        if (selNode.familiar != null && selNode.familiar.id != Guid.Empty)
                            newParentId = selNode.familiar.id;
                    }

                    // solo reassign si cambió realmente
                    if (persona.parentId != newParentId)
                    {
                        // ReassignParent maneja validaciones internas (evita ciclos, etc.)
                        _treeManager.ReassignParent(persona.id, newParentId);
                    }
                    // --- manejo de pareja en edición ---
                    // obtener el id del partner seleccionado (si cualquiera)
                    Guid? selectedPartnerId = null;
                    if (CmbPartnerSelect.SelectedItem is Node selPartnerNode)
                    {
                        if (selPartnerNode.familiar != null && selPartnerNode.familiar.id != Guid.Empty)
                            selectedPartnerId = selPartnerNode.familiar.id;
                    }

                    // Si seleccionaron la misma persona -> advertir y no asignar
                    if (selectedPartnerId.HasValue && selectedPartnerId.Value == persona.id)
                    {
                        MessageBox.Show("No puedes asignar la misma persona como pareja.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {
                        // Llamar a SetPartner (si selectedPartnerId == null -> se desasigna)
                        try
                        {
                            _treeManager.SetPartner(persona.id, selectedPartnerId);
                        }
                        catch (Exception exP)
                        {
                            MessageBox.Show("No se pudo asignar la pareja: " + exP.Message, "Pareja", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    // Recalcular aristas/distancias y actualizar UI
                    // Usamos los métodos públicos disponibles
                    try
                    {
                        _treeManager.GetEdgesWithWeights();
                    }
                    catch { /* ignorar */ }
                    try
                    {
                        _treeManager.ComputeAllDijkstras();
                    }
                    catch { /* ignorar */ }
                    try
                    {
                        _treeManager.ComputeMinMaxPairs();
                    }
                    catch { /* ignorar */ }

                    // refrescar UI y limpiar modo edición
                    LoadParentsCombo();
                    LoadPartnersCombo();
                    UpdateCanvasLayout();
                    _editingNode = null;
                    BtnSave_SetAddMode();
                    ClearFormFields();

                    MessageBox.Show("Persona editada correctamente.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Cambia texto del boton guardar a modo edición
        private void BtnSave_SetEditMode()
        {
            try
            {
                var btn = this.FindName("BtnSave") as Button;
                if (btn != null) btn.Content = "Guardar (Editar)";
            }
            catch { }
        }

        // Texto por defecto para añadir
        private void BtnSave_SetAddMode()
        {
            try
            {
                var btn = this.FindName("BtnSave") as Button;
                if (btn != null) btn.Content = "Guardar";
            }
            catch { }

        }
        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            // Buscar instancia existente del MainWindow y mostrarla, si existe
            foreach (Window w in Application.Current.Windows)
            {
                if (w is MainWindow main)
                {
                    main.Show();
                    this.Close();
                    return;
                }
            }

            // Si no existe, crear una nueva instancia y mostrar
            var newMain = new MainWindow();
            newMain.Show();
            this.Close();
        }

        #endregion

        private void ClearFormFields()
        {
            TxtNombre.Text = "";
            TxtCedula.Text = "";
            DpFechaNacimiento.SelectedDate = null;
            ChkVivo.IsChecked = true;
            TxtPlusCode.Text = "";
            LblLat.Text = "-";
            LblLon.Text = "-";
            ImgPhoto.Source = null;
            _photoFilePath = null;
            _lat = _lon = null;
            CmbParent.SelectedIndex = 0;
            CmbPartnerSelect.SelectedIndex = 0;
            TxtNotes.Text = "";
        }

        #region Canvas-based tree layout & drawing
        private const double NodeWidth = 160;
        private const double NodeHeight = 60;
        private const double HorizontalSpacing = 20;
        private const double VerticalSpacing = 60;

        // se encarga de dibujar las lineas y los nodos
        private void UpdateCanvasLayout()
        {
            try
            {
                TreeCanvas.Children.Clear();

                if (_treeManager == null) return;

                var roots = _treeManager.Roots.ToList();
                if (!roots.Any()) return;

                // calcular posiciones: recursivo, centrando subarboles
                double startX = 20;
                double y = 20;

                var positions = new Dictionary<Node, Point>();

                foreach (var r in roots)
                {
                    double subtreeWidth = MeasureSubtreeWidth(r);
                    double rootX = startX + subtreeWidth / 2.0;
                    PlaceSubtree(r, rootX, y, positions);
                    startX += subtreeWidth + HorizontalSpacing;
                }

                // ajustar canvas size
                double maxX = positions.Values.Any() ? positions.Values.Max(p => p.X) + NodeWidth + 20 : 800;
                double maxY = positions.Values.Any() ? positions.Values.Max(p => p.Y) + NodeHeight + 20 : 600;
                TreeCanvas.Width = Math.Max(maxX, 800);
                TreeCanvas.Height = Math.Max(maxY, 600);

                // dibujar aristas (líneas) primero
                foreach (var kv in positions)
                {
                    var parent = kv.Key;
                    var parentPos = kv.Value;
                    foreach (var child in parent.children)
                    {
                        if (!positions.ContainsKey(child)) continue;
                        var childPos = positions[child];

                        var p1 = new Point(parentPos.X + NodeWidth / 2.0, parentPos.Y + NodeHeight);
                        var p2 = new Point(childPos.X + NodeWidth / 2.0, childPos.Y);

                        var line = new Line
                        {
                            X1 = p1.X,
                            Y1 = p1.Y,
                            X2 = p2.X,
                            Y2 = p2.Y,
                            Stroke = Brushes.Gray,
                            StrokeThickness = 1.5
                        };
                        TreeCanvas.Children.Add(line);
                    }
                }
                // --- DIBUJAR LINEAS DE PAREJA (verde) ---
                try
                {
                    // evitamos dibujar duplicados; solo dibujamos si node.id < partnerId (orden por Guid)
                    var partnerDrawn = new HashSet<(Guid, Guid)>();

                    foreach (var kv in positions)
                    {
                        var node = kv.Key;
                        var pos = kv.Value;

                        if (node?.familiar == null) continue;
                        if (!node.familiar.partnerId.HasValue) continue;

                        var partnerId = node.familiar.partnerId.Value;

                        // evitar placeholder o id vacio
                        if (partnerId == Guid.Empty) continue;

                        // buscar partner node en positions
                        var partnerNode = positions.Keys.FirstOrDefault(n => n.familiar != null && n.familiar.id == partnerId);
                        if (partnerNode == null) continue; // partner no está en el layout (podría ser externo)

                        // normalizar orden para evitar duplicar la línea
                        var a = node.familiar.id;
                        var b = partnerId;
                        if (a.CompareTo(b) > 0) (a, b) = (b, a);

                        if (partnerDrawn.Contains((a, b))) continue;
                        partnerDrawn.Add((a, b));

                        var p1 = new Point(pos.X + NodeWidth / 2.0, pos.Y + NodeHeight / 2.0);
                        var p2pos = positions[partnerNode];
                        var p2 = new Point(p2pos.X + NodeWidth / 2.0, p2pos.Y + NodeHeight / 2.0);

                        // linea verde
                        var partnerLine = new Line
                        {
                            X1 = p1.X,
                            Y1 = p1.Y,
                            X2 = p2.X,
                            Y2 = p2.Y,
                            Stroke = Brushes.ForestGreen,
                            StrokeThickness = 3,
                            StrokeDashArray = new DoubleCollection() { 2, 2 }, // opcional: línea punteada
                            Opacity = 0.95
                        };
                        // poner por debajo de los nodos (se añaden antes de los rectángulos)
                        TreeCanvas.Children.Add(partnerLine);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error dibujando líneas de pareja: " + ex.Message);
                }

                // dibujar nodos (por encima)
                foreach (var kv in positions)
                {
                    var node = kv.Key;
                    var pos = kv.Value;
                    DrawNode(node, pos);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Canvas layout error: " + ex.Message);
            }
        }

        // Calcula el ancho total requerido por el subárbol (suma de anchos de hojas más spacing)
        private double MeasureSubtreeWidth(Node node)
        {
            if (node.children == null || node.children.Count == 0)
                return NodeWidth;

            double total = 0;
            foreach (var c in node.children)
            {
                total += MeasureSubtreeWidth(c);
            }
            total += HorizontalSpacing * Math.Max(0, node.children.Count - 1);
            return total;
        }

        // Coloca los nodos del subarbol centrados alrededor de centerX a partir de y
        private void PlaceSubtree(Node node, double centerX, double y, Dictionary<Node, Point> positions)
        {
            if (node.children == null || node.children.Count == 0)
            {
                // colocar hoja
                double x = centerX - NodeWidth / 2.0;
                positions[node] = new Point(x, y);
                return;
            }

            // calcular anchuras de hijos
            var widths = node.children.Select(c => MeasureSubtreeWidth(c)).ToList();
            double totalWidth = widths.Sum() + HorizontalSpacing * (widths.Count - 1);

            double left = centerX - totalWidth / 2.0;
            for (int i = 0; i < node.children.Count; i++)
            {
                var child = node.children[i];
                double w = widths[i];
                double childCenter = left + w / 2.0;
                PlaceSubtree(child, childCenter, y + NodeHeight + VerticalSpacing, positions);
                left += w + HorizontalSpacing;
            }

            // colocar nodo actual centrado encima de sus hijos
            double myX = centerX - NodeWidth / 2.0;
            positions[node] = new Point(myX, y);
        }
        // se encarga de dibujar los nodos
        private void DrawNode(Node node, Point pos)
        {
            // rectángulo contenedor (Border)
            var rectBorder = new Border
            {
                Width = NodeWidth,
                Height = NodeHeight,
                Background = Brushes.WhiteSmoke,
                BorderBrush = Brushes.DarkGray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6)
            };

            // layout interno: imagen a la izquierda y textos a la derecha
            var container = new DockPanel { LastChildFill = true };

            // Image control (thumbnail)
            var imgControl = new Image
            {
                Width = NodeHeight - 12, // dejar margen interior
                Height = NodeHeight - 12,
                Stretch = Stretch.UniformToFill,
                Margin = new Thickness(0, 0, 8, 0),
            };

            // Intentar cargar imagen desde cache / disco
            string? imgPath = node.familiar?.photoFileName;
            BitmapImage? bmp = null;
            if (!string.IsNullOrWhiteSpace(imgPath))
            {
                try
                {
                    if (_imageCache.ContainsKey(imgPath))
                    {
                        bmp = _imageCache[imgPath];
                    }
                    else if (File.Exists(imgPath))
                    {
                        // cargar sin bloquear archivo
                        var bi = new BitmapImage();
                        using (var fs = new FileStream(imgPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            bi.BeginInit();
                            bi.CacheOption = BitmapCacheOption.OnLoad;
                            bi.StreamSource = fs;
                            bi.DecodePixelWidth = (int)(NodeHeight - 12); // decode a tamaño pequeño
                            bi.EndInit();
                            bi.Freeze();
                        }
                        _imageCache[imgPath] = bi;
                        bmp = bi;
                    }
                }
                catch
                {
                    bmp = null;
                }
            }

            if (bmp != null)
            {
                imgControl.Source = bmp;
                // envolver la imagen en un Border para esquinas redondeadas
                var imgHolder = new Border
                {
                    Width = imgControl.Width,
                    Height = imgControl.Height,
                    CornerRadius = new CornerRadius(4),
                    ClipToBounds = true,
                    Child = imgControl
                };
                DockPanel.SetDock(imgHolder, Dock.Left);
                container.Children.Add(imgHolder);
            }
            else
            {
                // placeholder (círculo o rect con iniciales)
                var place = new Border
                {
                    Width = NodeHeight - 12,
                    Height = NodeHeight - 12,
                    CornerRadius = new CornerRadius((NodeHeight - 12) / 2),
                    Background = Brushes.LightGray,
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 8, 0)
                };

                // iniciales
                var initials = GetInitials(node.familiar?.name);
                var tbInit = new TextBlock
                {
                    Text = initials,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White
                };
                place.Child = tbInit;
                DockPanel.SetDock(place, Dock.Left);
                container.Children.Add(place);
            }

            // Texto: nombre y ownId
            var textStack = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
            var tbName = new TextBlock
            {
                Text = node.familiar?.name ?? "(sin nombre)",
                FontWeight = FontWeights.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var tbId = new TextBlock
            {
                Text = node.familiar?.ownId ?? "",
                FontSize = 11,
                Opacity = 0.8
            };
            textStack.Children.Add(tbName);
            textStack.Children.Add(tbId);

            container.Children.Add(textStack);
            rectBorder.Child = container;

            // position en canvas
            Canvas.SetLeft(rectBorder, pos.X);
            Canvas.SetTop(rectBorder, pos.Y);

            // Tooltip con más datos
            rectBorder.ToolTip = $"{node.familiar?.name}\nOwnId: {node.familiar?.ownId}\nLat/Lon: {(node.familiar?.HasCoordinates() == true ? $"{node.familiar.lat:F6}, {node.familiar.lon:F6}" : "—")}";

            // click: cargar en formulario para editar
            rectBorder.MouseLeftButtonDown += (s, e) =>
            {
                LoadNodeIntoForm(node);
            };

            TreeCanvas.Children.Add(rectBorder);
        }

        // helper: devuelve iniciales a partir de nombre
        private string GetInitials(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "?";
            try
            {
                var parts = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1) return parts[0].Substring(0, 1).ToUpperInvariant();
                return (parts[0].Substring(0, 1) + parts[parts.Length - 1].Substring(0, 1)).ToUpperInvariant();
            }
            catch
            {
                return name.Substring(0, Math.Min(1, name.Length)).ToUpperInvariant();
            }
        }


        #endregion

        #region Load node into form (edit)
        // se encarga de colocar la informacion en el formulario en caso de querer editar un nodo
        private void LoadNodeIntoForm(Node node)
        {
            if (node == null) return;

            _editingNode = node;

            // llenar campos
            TxtNombre.Text = node.familiar.name ?? "";
            TxtCedula.Text = node.familiar.ownId ?? "";
            DpFechaNacimiento.SelectedDate = node.familiar.birthdate == DateTime.MinValue ? (DateTime?)null : node.familiar.birthdate;
            ChkVivo.IsChecked = true; // no tenemos info de fallecido en Persona, ajustar si la tuvieras
            TxtPlusCode.Text = node.familiar.addresPlusCode ?? "";
            _lon = node.familiar.lon;
            _lat = node.familiar.lat;
            LblLat.Text = node.familiar.lat.HasValue ? node.familiar.lat.Value.ToString("F6") : "-";
            LblLon.Text = node.familiar.lon.HasValue ? node.familiar.lon.Value.ToString("F6") : "-";

            // foto: cargar si existe path
            if (!string.IsNullOrWhiteSpace(node.familiar.photoFileName) && File.Exists(node.familiar.photoFileName))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(node.familiar.photoFileName);
                bmp.EndInit();
                ImgPhoto.Source = bmp;
                _photoFilePath = node.familiar.photoFileName;
            }
            else
            {
                ImgPhoto.Source = null;
                _photoFilePath = null;
            }

            // seleccionar en combo el parent actual (si existe)
            if (node.familiar.parentId.HasValue)
            {
                Guid pid = node.familiar.parentId.Value;
                for (int i = 0; i < CmbParent.Items.Count; i++)
                {
                    if (CmbParent.Items[i] is Node n && n.familiar != null && n.familiar.id == pid)
                    {
                        CmbParent.SelectedIndex = i;
                        break;
                    }
                }
            }
            else
            {
                // seleccionar placeholder
                CmbParent.SelectedIndex = 0;
            }
            // RECARGAR lista de partners excluyendo el nodo que estamos editando
            LoadPartnersCombo(node.familiar.id);

            // seleccionar pareja en CmbPartnerSelect si existe
            if (node.familiar.partnerId.HasValue)
            {
                Guid partnerId = node.familiar.partnerId.Value;

                // Buscar en ItemsSource (que contiene Node) el que tenga familiar.id == partnerId
                var items = CmbPartnerSelect.ItemsSource as IEnumerable<Node>;
                if (items != null)
                {
                    var match = items.FirstOrDefault(n => n != null && n.familiar != null && n.familiar.id == partnerId);
                    if (match != null)
                    {
                        CmbPartnerSelect.SelectedItem = match;
                    }
                    else
                    {
                        // si no está (partner no presente en el árbol cargado o była excluido), dejar "(Ninguna)"
                        CmbPartnerSelect.SelectedIndex = 0;
                    }
                }
                else
                {
                    CmbPartnerSelect.SelectedIndex = 0;
                }
            }
            else
            {
                // seleccionar placeholder "Ninguna"
                CmbPartnerSelect.SelectedIndex = 0;
            }


            // cambiar botón guardar a modo editar
            BtnSave_SetEditMode();
        }
        #endregion

        #region IDisposable
        public void Dispose()
        {
            UnsubscribeFromTreeManager();
        }
        #endregion
    }
}

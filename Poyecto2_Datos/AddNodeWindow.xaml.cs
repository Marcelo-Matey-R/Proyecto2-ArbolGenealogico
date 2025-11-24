using ArbolGenealogico.Core.Managers;
using ArbolGenealogico.Domain.Models;
using ArbolGenealogico.Infraestructure.Services;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

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


        // Nodo que estamos editando
        private Node? _editingNode = null;

        public AddNodeWindow()
        {
            InitializeComponent();

            _treeManager = ResolveTreeManager();
            SubscribeToTreeManager();

            // constructor
            LoadParentsCombo();
            UpdateCanvasLayout();
            LoadPartnersCombo();
        }

        #region Crear una instancia de TreeManager
        private TreeManager? ResolveTreeManager()
        {
            try
            {
                if (Application.Current?.Properties != null && Application.Current.Properties.Contains("TreeManager"))
                {
                    return Application.Current.Properties["TreeManager"] as TreeManager;
                }

                // Crear nuevo TreeManager y guardarlo
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

        #region UI helpers
        // Carga los nodos al combobox de los padres
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
        // Carga los nodos al selector de parejas
        private void LoadPartnersCombo(Guid? excludeId = null)
        {
            try
            {
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

                    // añade solo nodos válidos
                    foreach (var n in allNodes)
                    {
                        // opcional: filtrar nodos con id == Guid.Empty por seguridad
                        if (n.familiar == null || n.familiar.id == Guid.Empty) continue;

                        // excluir el nodo que estamos editando
                        if (excludeId.HasValue && n.familiar.id == excludeId.Value) continue;

                        items.Add(n);
                    }
                }

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

        // Permitir sólo dígitos durante la escritura
        private void TxtAge_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            // permitir sólo dígitos
            e.Handled = !e.Text.All(char.IsDigit);
        }

        // Evitar pegar texto no numérico
        private void TxtAge_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(DataFormats.Text))
            {
                var text = e.DataObject.GetData(DataFormats.Text) as string ?? "";
                if (!text.All(char.IsDigit))
                {
                    e.CancelCommand();
                }
            }
            else
            {
                e.CancelCommand();
            }
        }
        // Valida si los inputs tienen los caracteres correctos
        private bool ValidateForm(out string error)
        {
            error = "";

            var nombre = (TxtNombre.Text ?? "").Trim();
            var ownId = (TxtCedula.Text ?? "").Trim();
            var plus = (TxtPlusCode.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(nombre) || nombre.Any(char.IsDigit))
            {
                error = "Nombre inválido. No puede contener números.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(ownId) || !ownId.All(char.IsDigit))
            {
                error = "Cédula inválida. Use sólo números.";
                return false;
            }

            // Requerimiento del proyecto: coordenadas deben existir (o se debe convertir PlusCode)
            var calc = new CalcDistance();
            if (!calc.TryConvertPlusCode(plus, out double lon, out double lat) && (_lon == null || _lat == null))
            {
                error = "Coordenadas inválidas. Ingrese coordenadas o un Plus Code válido.";
                return false;
            }

            error = null;
            return true;
        }

        #endregion

        #region Photo and pluscode controls
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
                LoadParentsCombo();
                LoadPartnersCombo();
            }
            else
            {
                this.Close();
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!ValidateForm(out string validationError))
                {
                    MessageBox.Show(validationError, "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var nombre = TxtNombre.Text.Trim();
                var ownId = TxtCedula.Text.Trim();
                var birth = DpFechaNacimiento.SelectedDate ?? DateTime.MinValue;
                var plus = TxtPlusCode.Text?.Trim() ?? "";
                var calc = new CalcDistance();
                var calcAge = new Persona();

                // Determinar edad
                int age = 0;
                var ageText = (TxtAge?.Text ?? "").Trim();
                if (!string.IsNullOrEmpty(ageText)) int.TryParse(ageText, out age);
                else if (birth != DateTime.MinValue)
                {
                    age = calcAge.CalcAge(birth);
                }

                // Geocoding fallback: obtener coords desde pluscode si no fueron convertidas manualmente
                if ((_lon == null || _lat == null) && !string.IsNullOrWhiteSpace(plus))
                {
                    if (calc.TryConvertPlusCode(plus, out double lon, out double lat))
                    {
                        _lon = lon; _lat = lat;
                    }
                }

                if (_editingNode == null)
                {
                    // CREAR
                    var persona = new Persona(Guid.NewGuid(), nombre, age, birth, _photoFilePath ?? "", plus, _lon, _lat, null, null, false)
                    {
                        ownId = ownId
                    };

                    Guid? parentId = null;
                    if (CmbParent.SelectedItem is Node selNode && selNode.familiar != null && selNode.familiar.id != Guid.Empty)
                        parentId = selNode.familiar.id;

                    Guid? selectedPartnerId = null;
                    if (CmbPartnerSelect.SelectedItem is Node selPartnerNode && selPartnerNode.familiar != null && selPartnerNode.familiar.id != Guid.Empty)
                        selectedPartnerId = selPartnerNode.familiar.id;

                    if (_treeManager == null)
                    {
                        MessageBox.Show("No se pudo resolver TreeManager.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    _treeManager.AddPerson(persona, parentId);

                    if (selectedPartnerId.HasValue)
                    {
                        try
                        {
                            _treeManager.SetPartner(persona.id, selectedPartnerId.Value);
                        }
                        catch (Exception exPartner)
                        {
                            MessageBox.Show("No se pudo asignar la pareja: " + exPartner.Message, "Pareja", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }

                    MessageBox.Show("Persona agregada correctamente.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    // EDITAR
                    var persona = _editingNode.familiar;
                    if (persona == null) return;

                    persona.name = nombre;
                    persona.ownId = ownId;
                    persona.birthdate = birth;
                    persona.age = age;
                    persona.photoFileName = _photoFilePath ?? persona.photoFileName;
                    persona.addresPlusCode = plus;

                    if (_lat.HasValue && _lon.HasValue)
                    {
                        persona.lon = _lon;
                        persona.lat = _lat;
                    }
                    else if ((!persona.HasCoordinates() || persona.lat == null || persona.lon == null) && !string.IsNullOrWhiteSpace(plus))
                    {
                        if (calc.TryConvertPlusCode(plus, out double Lon, out double Lat))
                        {
                            persona.lon = Lon;
                            persona.lat = Lat;
                        }
                    }

                    // cambio de padre
                    Guid? newParentId = null;
                    if (CmbParent.SelectedItem is Node selNode2 && selNode2.familiar != null && selNode2.familiar.id != Guid.Empty)
                        newParentId = selNode2.familiar.id;

                    if (persona.parentId != newParentId)
                    {
                        try { _treeManager.ReassignParent(persona.id, newParentId); } catch (Exception exReassign) { MessageBox.Show("No se pudo reasignar padre: " + exReassign.Message, "Advertencia", MessageBoxButton.OK, MessageBoxImage.Warning); }
                    }

                    // pareja
                    Guid? selectedPartnerIdEdit = null;
                    if (CmbPartnerSelect.SelectedItem is Node selPartnerNodeEdit && selPartnerNodeEdit.familiar != null && selPartnerNodeEdit.familiar.id != Guid.Empty)
                        selectedPartnerIdEdit = selPartnerNodeEdit.familiar.id;

                    if (selectedPartnerIdEdit.HasValue)
                    {
                        if (selectedPartnerIdEdit.Value == persona.id)
                        {
                            MessageBox.Show("No puedes asignar la misma persona como pareja.", "Validación", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        else
                        {
                            try
                            {
                                _treeManager.SetPartner(persona.id, selectedPartnerIdEdit.Value);
                            }
                            catch (Exception exP) { MessageBox.Show("No se pudo asignar la pareja: " + exP.Message, "Pareja", MessageBoxButton.OK, MessageBoxImage.Warning); }
                        }
                    }
                    else
                    {
                        try { _treeManager.SetPartner(persona.id, null); } catch { }
                    }

                    MessageBox.Show("Persona editada correctamente.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
                    _editingNode = null;
                    BtnSave_SetAddMode();
                }

                // recalcular y refrescar UI
                try { _treeManager.GetEdgesWithWeights(); } catch { }
                try { _treeManager.ComputeAllDijkstras(); } catch { }
                try { _treeManager.ComputeMinMaxPairs(); } catch { }

                LoadParentsCombo();
                LoadPartnersCombo();
                UpdateCanvasLayout();
                ClearFormFields();
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
            TxtPlusCode.Text = "";
            LblLat.Text = "-";
            LblLon.Text = "-";
            ImgPhoto.Source = null;
            _photoFilePath = null;
            _lat = _lon = null;
            CmbParent.SelectedIndex = 0;
            CmbPartnerSelect.SelectedIndex = 0;
            TxtNotes.Text = "";
            TxtAge.Text = "";
        }

        #region Graficar los nodos del arbol y cargar los datos de una persona al editar
        private const double NodeWidth = 160;
        private const double NodeHeight = 60;
        private const double HorizontalSpacing = 20;
        private const double VerticalSpacing = 60;

        // Actualiza y crea el arbol visualmente
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

                // ---------- Ajuste visual: colocar pareja siempre al lado derecho del anchor (mejorado) ----------
                try
                {
                    var keys = positions.Keys.ToList();
                    var processedPairs = new HashSet<(Guid, Guid)>();
                    double gap = 12.0; // separación entre nodos cuando se colocan en fila

                    // Helper: rectángulo ocupado por un nodo en posiciones (para detectar colisiones)
                    Rect NodeRect(Point p) => new Rect(p.X, p.Y, NodeWidth, NodeHeight);

                    // Mueve recursivamente a la derecha cualquier nodo que colisione con targetRect (solo nodos en mismo nivel)
                    void ShiftRightRecursive(Rect targetRect, double step, HashSet<Node> visited, int depth = 0)
                    {
                        if (depth > 200) return; // safety
                        var colliders = positions.Where(kv =>
                        {
                            var r = NodeRect(kv.Value);
                            // considerar sólo nodos que intersectan y que no son parte del targetRect exacto
                            return r.IntersectsWith(targetRect);
                        })
                            .Select(kv => kv.Key)
                            .Where(n => !visited.Contains(n))
                            .ToList();

                        foreach (var col in colliders)
                        {
                            // no forzamos mover anchors con hijos si son anchors en parejas (respetamos jerarquía)
                            if (col.children != null && col.children.Count > 0) continue;

                            visited.Add(col);

                            // desplazar este collider a la derecha
                            var oldPos = positions[col];
                            var newPos = new Point(oldPos.X + step, oldPos.Y);
                            positions[col] = newPos;

                            // construir nuevo rect para detectar siguiente colisión
                            var newRect = NodeRect(newPos);

                            // recursividad: si al moverlo choca con otros, moverlos también
                            ShiftRightRecursive(newRect, step, visited, depth + 1);
                        }
                    }

                    foreach (var node in keys)
                    {
                        if (node?.familiar?.partnerId == null) continue;

                        var a = node.familiar.id;
                        var b = node.familiar.partnerId.Value;
                        var key = a.CompareTo(b) <= 0 ? (a, b) : (b, a);
                        if (processedPairs.Contains(key)) continue;

                        var nodeA = keys.FirstOrDefault(n => n.familiar != null && n.familiar.id == key.Item1);
                        var nodeB = keys.FirstOrDefault(n => n.familiar != null && n.familiar.id == key.Item2);
                        if (nodeA == null || nodeB == null) { processedPairs.Add(key); continue; }

                        var posA = positions[nodeA];
                        var posB = positions[nodeB];

                        // Elegir ancla: preferir el que tenga hijos (no mover padres), si ninguno tiene hijos usar el más a la izquierda
                        bool AHasChildren = nodeA.children != null && nodeA.children.Count > 0;
                        bool BHasChildren = nodeB.children != null && nodeB.children.Count > 0;

                        Node anchor = nodeA;
                        Node mover = nodeB;
                        Point anchorPos = posA;
                        Point moverPos = posB;

                        if (AHasChildren && !BHasChildren)
                        {
                            anchor = nodeA; mover = nodeB; anchorPos = posA; moverPos = posB;
                        }
                        else if (BHasChildren && !AHasChildren)
                        {
                            anchor = nodeB; mover = nodeA; anchorPos = posB; moverPos = posA;
                        }
                        else
                        {
                            if (posA.X <= posB.X)
                            {
                                anchor = nodeA; mover = nodeB; anchorPos = posA; moverPos = posB;
                            }
                            else
                            {
                                anchor = nodeB; mover = nodeA; anchorPos = posB; moverPos = posA;
                            }
                        }

                        // posición objetivo: justo a la derecha del anchor
                        double targetX = anchorPos.X + NodeWidth + gap;
                        double targetY = anchorPos.Y;
                        var targetRect = NodeRect(new Point(targetX, targetY));

                        // Si el mover ya está en targetX aproximado -> solo alinear Y
                        if (Math.Abs(moverPos.X - targetX) < 1.0)
                        {
                            positions[mover] = new Point(moverPos.X, targetY);
                            processedPairs.Add(key);
                            continue;
                        }

                        // Detectar colisiones en targetRect con nodos existentes (excepto mover y anchor)
                        var existingRects = positions.ToDictionary(kv => kv.Key, kv => NodeRect(kv.Value));
                        var colliding = existingRects.Where(kv => kv.Key != mover && kv.Key != anchor && kv.Value.IntersectsWith(targetRect))
                                                     .Select(kv => kv.Key)
                                                     .ToList();

                        if (!colliding.Any())
                        {
                            // libre, asignar directamente
                            positions[mover] = new Point(targetX, targetY);
                            processedPairs.Add(key);
                            continue;
                        }

                        // Hay colisiones: en lugar de empujar mover más a la derecha, vamos a desplazar recursivamente
                        // los nodos que colisionan (y los que colisionen a su vez) hacia la derecha por (NodeWidth + gap).
                        var visited = new HashSet<Node>();
                        double step = NodeWidth + gap;
                        // Movemos solo nodos que están en el mismo nivel Y (aprox), para no afectar otras filas
                        // Crear rect objetivo inicial y desplazar colliders recursivamente
                        ShiftRightRecursive(targetRect, step, visited);

                        // Tras desplazar a los colliders, ahora debería quedar libre; asignar mover en target
                        positions[mover] = new Point(targetX, targetY);

                        processedPairs.Add(key);
                    }
                }
                catch (Exception exPos)
                {
                    Console.WriteLine("Ajuste de posiciones de pareja error (lado derecho, con shift): " + exPos.Message);
                }




                // dibujar aristas (líneas) primero: SOLO si child.familiar.parentId apunta a este parent
                foreach (var kv in positions)
                {
                    var parent = kv.Key;
                    var parentPos = kv.Value;

                    foreach (var child in parent.children)
                    {
                        if (!positions.ContainsKey(child)) continue;

                        if (child.familiar == null || parent.familiar == null) continue;

                        if (!child.familiar.parentId.HasValue || child.familiar.parentId.Value != parent.familiar.id)
                            continue;

                        var childPos = positions[child];

                        var p1 = new Point(parentPos.X + NodeWidth / 2.0, parentPos.Y + NodeHeight);
                        var p2 = new Point(childPos.X + NodeWidth / 2.0, childPos.Y);

                        var line = new Line
                        {
                            X1 = p1.X,
                            Y1 = p1.Y,
                            X2 = p2.X,
                            Y2 = p2.Y,
                            Stroke = Brushes.White,
                            StrokeThickness = 1.5
                        };
                        TreeCanvas.Children.Add(line);
                    }
                }

                // ---------- DIBUJAR LINEAS DE PAREJA (verde) ----------
                try
                {
                    var partnerDrawn = new HashSet<(Guid, Guid)>();
                    foreach (var kv in positions)
                    {
                        var node = kv.Key;
                        var pos = kv.Value;

                        if (node?.familiar == null) continue;
                        if (!node.familiar.partnerId.HasValue) continue;

                        var partnerId = node.familiar.partnerId.Value;
                        if (partnerId == Guid.Empty) continue;

                        var partnerNode = positions.Keys.FirstOrDefault(n => n.familiar != null && n.familiar.id == partnerId);
                        if (partnerNode == null) continue;

                        var a = node.familiar.id;
                        var b = partnerId;
                        if (a.CompareTo(b) > 0) (a, b) = (b, a);
                        if (partnerDrawn.Contains((a, b))) continue;
                        partnerDrawn.Add((a, b));

                        var p1 = new Point(pos.X + NodeWidth / 2.0, pos.Y + NodeHeight / 2.0);
                        var p2pos = positions[partnerNode];
                        var p2 = new Point(p2pos.X + NodeWidth / 2.0, p2pos.Y + NodeHeight / 2.0);

                        var partnerLine = new Line
                        {
                            X1 = p1.X,
                            Y1 = p1.Y,
                            X2 = p2.X,
                            Y2 = p2.Y,
                            Stroke = Brushes.Red,
                            StrokeThickness = 3,
                        };
                        TreeCanvas.Children.Add(partnerLine);


                    }
                }
                catch (Exception exPartner)
                {
                    Console.WriteLine("Error dibujando líneas de pareja: " + exPartner.Message);
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

        private void DrawNode(Node node, Point pos)
        {
            // rectángulo contenedor (Border)
            var rectBorder = new Border
            {
                Width = NodeWidth,
                Height = NodeHeight,
                Background = Brushes.Black,
                BorderBrush = Brushes.Cyan,
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
                    BorderBrush = Brushes.White,
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
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var tbId = new TextBlock
            {
                Text = node.familiar?.ownId ?? "",
                Foreground = Brushes.White,
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
            rectBorder.ToolTip = $"{node.familiar?.name}\nCedula: {node.familiar?.ownId}\nLat/Lon: {(node.familiar?.HasCoordinates() == true ? $"{node.familiar.lat:F6}, {node.familiar.lon:F6}" : "—")}";

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

        #region Cargar nodos al formulario al editar
        private void LoadNodeIntoForm(Node node)
        {
            if (node == null) return;

            _editingNode = node;

            // llenar campos
            TxtNombre.Text = node.familiar.name ?? "";
            TxtCedula.Text = node.familiar.ownId ?? "";
            DpFechaNacimiento.SelectedDate = node.familiar.birthdate == DateTime.MinValue ? (DateTime?)null : node.familiar.birthdate;
            TxtPlusCode.Text = node.familiar.addresPlusCode ?? "";
            _lon = node.familiar.lon;
            _lat = node.familiar.lat;
            // edad
            TxtAge.Text = node.familiar.age.ToString();

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

            // seleccionar en combo el parent actual
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
            // seleccionar pareja en CmbPartnerSelect si existe
            // recargar lista de partners excluyendo el nodo que estamos editando
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

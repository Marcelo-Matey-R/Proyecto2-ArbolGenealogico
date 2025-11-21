public static class MarkerImageHelper
{
    // Carpeta de cache donde se guardan los PNG circulares
    public static string GetCacheFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "MapsuiMarkerCache");
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        return folder;
    }

    // Genera un nombre único basado en path + size
    private static string HashFilename(string sourcePath, int px)
    {
        using var sha = SHA256.Create();
        var input = Encoding.UTF8.GetBytes(sourcePath + "|" + px.ToString());
        var hash = sha.ComputeHash(input);
        var hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        return $"marker_{hex}_{px}.png";
    }

    /// <summary>
    /// Devuelve la ruta al PNG circular (generado o en cache) para la imagen fuente.
    /// La imagen resultante tendrá tamaño outputSizePx x outputSizePx y fondo transparente.
    /// </summary>
    public static string EnsureCircularPng(string sourceImagePath, int outputSizePx = 64)
    {
        if (!File.Exists(sourceImagePath)) throw new FileNotFoundException("Imagen fuente no encontrada", sourceImagePath);

        var cacheFolder = GetCacheFolder();
        var filename = HashFilename(Path.GetFullPath(sourceImagePath), outputSizePx);
        var outPath = Path.Combine(cacheFolder, filename);

        if (File.Exists(outPath)) return outPath; // ya existe -> reusar

        // Crear circular PNG
        using var src = System.Drawing.Image.FromFile(sourceImagePath);
        // recortar al square central
        int side = Math.Min(src.Width, src.Height);
        int sx = (src.Width - side) / 2;
        int sy = (src.Height - side) / 2;

        using var bmp = new Bitmap(outputSizePx, outputSizePx, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);

            // crear clip circular
            using var path = new GraphicsPath();
            path.AddEllipse(0, 0, outputSizePx, outputSizePx);
            g.SetClip(path);

            // dibujar la parte central escalada
            var destRect = new Rectangle(0, 0, outputSizePx, outputSizePx);
            g.DrawImage(src, destRect, sx, sy, side, side, GraphicsUnit.Pixel);
        }

        // Guardar como PNG con transparencia
        bmp.Save(outPath, ImageFormat.Png);

        return outPath;
    }

    /// <summary>
    /// Aplica la imagen circular (generada a partir de sourceImagePath) como estilo de marcador
    /// sobre la PointFeature indicada. Si la imagen ya existe en cache se reutiliza.
    /// - outputSizePx: tamaño en píxeles de la imagen circular (ej. 48, 64).
    /// - anchorCentered: si true usa RelativeOffset(0.5,0.5) (útil para caras). Si false usa (0.5,1.0) (pies).
    /// </summary>
    public static void ApplyCircularMarkerImageToFeature(
        MemoryLayer markerLayer,
        PointFeature feature,
        string sourceImagePath,
        int outputSizePx = 64,
        bool anchorCentered = true,
        MapControl? mapControl = null)
    {
        if (markerLayer == null) throw new ArgumentNullException(nameof(markerLayer));
        if (feature == null) throw new ArgumentNullException(nameof(feature));
        if (!File.Exists(sourceImagePath)) throw new FileNotFoundException("Imagen fuente no encontrada", sourceImagePath);

        // 1) generar o recuperar circular png
        string circularPath = EnsureCircularPng(sourceImagePath, outputSizePx);

        // 2) Crear Mapsui Image (usamos file:// URI)
        var mapsuiImage = new Mapsui.Styles.Image
        {
            Source = new Uri(Path.GetFullPath(circularPath)).AbsoluteUri
        };

        // 3) ImageStyle: como ya generamos la imagen al tamaño deseado, usamos SymbolScale = 1.0
        var imageStyle = new ImageStyle
        {
            Image = mapsuiImage,
            SymbolScale = 1.0,
            RelativeOffset = anchorCentered ? new RelativeOffset(0.5, 0.5) : new RelativeOffset(0.5, 1.0),
            RotateWithMap = false
        };

        // 4) Aplicar el estilo (limpiamos estilos previos)
        feature.Styles.Clear();
        feature.Styles.Add(imageStyle);

        // 5) Notificar al layer / map que los datos cambiaron para forzar redraw
        try
        {
            // si existe DataHasChanged (algunas versiones) la llamamos
            var meth = markerLayer.GetType().GetMethod("DataHasChanged");
            if (meth != null) meth.Invoke(markerLayer, null);
        }
        catch { /* ignore */ }

        // 6) Si recibimos MapControl lo refrescamos (opcional)
        if (mapControl != null)
        {
            try { mapControl.Refresh(); } catch { }
        }
    }
}

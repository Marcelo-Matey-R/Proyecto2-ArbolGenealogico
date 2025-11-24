using Mapsui.Layers;                  // MemoryLayer, PointFeature
using Mapsui.Styles;                  // SymbolStyle, Brush, Pen, ImageStyle, Image
using Mapsui.UI.Wpf;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ProyectoDatos22.ArbolGenealogico.Infraestructure.Services
{
    public static class MarkerImageHelper
    {
        // devuelve la carpeta de cache para los PNG circulares
        public static string GetCacheFolder()
        {
            var folder = Path.Combine(Path.GetTempPath(), "MapsuiMarkerCache");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            return folder;
        }


        // genera un nombre de archivo hash para la imagen cacheada
        private static string HashFilename(string sourcePath, int px)
        {
            using var sha = SHA256.Create();
            var input = Encoding.UTF8.GetBytes(Path.GetFullPath(sourcePath) + "|" + px.ToString());
            var hash = sha.ComputeHash(input);
            var hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            return $"marker_{hex}_{px}.png";
        }


        // Genera o devuelve el PNG circular cacheado para la imagen fuente.
        public static string EnsureCircularPng(string sourceImagePath, int outputSizePx = 32)
        {
            if (!File.Exists(sourceImagePath)) throw new FileNotFoundException("Imagen fuente no encontrada", sourceImagePath);

            var cacheFolder = GetCacheFolder();
            var filename = HashFilename(sourceImagePath, outputSizePx);
            var outPath = Path.Combine(cacheFolder, filename); // ruta completa del PNG cacheado

            // si ya existe, devolverla
            if (File.Exists(outPath)) return outPath;

            // generar el PNG circular
            using var src = System.Drawing.Image.FromFile(sourceImagePath);
            int side = Math.Min(src.Width, src.Height);
            int sx = (src.Width - side) / 2;
            int sy = (src.Height - side) / 2;

            using var bmp = new Bitmap(outputSizePx, outputSizePx, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);

                using var path = new GraphicsPath();
                path.AddEllipse(0, 0, outputSizePx, outputSizePx);
                g.SetClip(path);

                var destRect = new Rectangle(0, 0, outputSizePx, outputSizePx);
                g.DrawImage(src, destRect, sx, sy, side, side, GraphicsUnit.Pixel);
            }

            bmp.Save(outPath, ImageFormat.Png);
            return outPath;
        }


        // Aplica la imagen circular a la feature.
        public static void ApplyCircularMarkerImageToFeature(MemoryLayer markerLayer, PointFeature feature, string sourceImagePath, int outputSizePx = 32, bool anchorCentered = true, MapControl? mapControl = null)
        {
            if (markerLayer == null) throw new ArgumentNullException(nameof(markerLayer));
            if (feature == null) throw new ArgumentNullException(nameof(feature));
            if (!File.Exists(sourceImagePath)) throw new FileNotFoundException("Imagen fuente no encontrada", sourceImagePath);

            string circularPath = EnsureCircularPng(sourceImagePath, outputSizePx);

            // crear el estilo de imagen
            var mapsuiImage = new Mapsui.Styles.Image
            {
                Source = new Uri(Path.GetFullPath(circularPath)).AbsoluteUri
            };

            var imageStyle = new ImageStyle
            {
                Image = mapsuiImage,
                SymbolScale = 1.0,
                RelativeOffset = anchorCentered ? new RelativeOffset(0.1,0.1) : new RelativeOffset(0.1, 0.1),
                RotateWithMap = false
            };

            feature.Styles.Clear();
            feature.Styles.Add(imageStyle);

            // notificar al layer que los datos han cambiado
            try
            {
                var meth = markerLayer.GetType().GetMethod("DataHasChanged");
                if (meth != null) meth.Invoke(markerLayer, null);
            }
            catch { }

            if (mapControl != null)
            {
                try { mapControl.Refresh(); } catch { }
            }
        }
    }
}

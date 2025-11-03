using System.Globalization;
using ArbolGenealogico.Domain.Contracts;

namespace ArbolGenealogico.Infraestructure.Services
{
    public class ImageServiceFileSystem : IImageService
    {
        private readonly string _photosFolder;
        private readonly HashSet<string> _allowedExt = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif" };

        public ImageServiceFileSystem(string photosFolder) { _photosFolder = photosFolder; }

        public string SaveUploadedPhoto(string sourceFilePath)
        {
            // implementar copia y devolver nombre relativo
            var fileName = Path.GetFileName(sourceFilePath);
            var dest = Path.Combine(_photosFolder, fileName);
            File.Copy(sourceFilePath, dest, overwrite: true);
            return fileName;
        }

        public string ResolvePhotoPath(string photoFileName)
        {
            if (Path.IsPathRooted(photoFileName)) return photoFileName;
            return Path.Combine(_photosFolder, photoFileName);
        }

        public bool IsValidImageFileName(string fileName)
        {
            var ext = Path.GetExtension(fileName);
            return !string.IsNullOrEmpty(ext) && _allowedExt.Contains(ext);
        }
    }
}
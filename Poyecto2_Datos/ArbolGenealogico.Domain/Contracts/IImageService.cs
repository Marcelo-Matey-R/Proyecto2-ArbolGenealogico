using System.Globalization;

namespace ArbolGenealogico.Domain.Contracts
{
    public interface IImageService
    {
        ///Guarda el archivo subido en la carpeta de fotos y devuelve el photoFileName (relativo)
        string SaveUploadedPhoto(string sourceFilePath);

        ///Resuelve la ruta absoluta de un photoFileName relativo (o devuelve la absoluta si ya lo es)
        string ResolvePhotoPath(string photoFileName);

        ///Valida si el nombre/extension es de imagen permitida
        bool IsValidImageFileName(string fileName);
    }
}
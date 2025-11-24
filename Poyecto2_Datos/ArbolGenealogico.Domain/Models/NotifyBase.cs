using System.Globalization;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ArbolGenealogico.Domain.Models
{
    public abstract class NotifyBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged; // Evento para notificar cambios en las propiedades
        // Método auxiliar para establecer el valor de una propiedad y notificar el cambio
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
        // Método para notificar que una propiedad ha cambiado
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}

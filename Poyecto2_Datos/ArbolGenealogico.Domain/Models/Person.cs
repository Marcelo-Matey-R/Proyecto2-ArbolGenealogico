using System.Globalization;
using System.Text.Json.Serialization;
using ArbolGenealogico.Domain.Models;
using ArbolGenealogico.Infraestructure.Services;
namespace ArbolGenealogico.Domain.Models
{
    public class Persona : NotifyBase
    {
        public Persona()
        {
            this.id = Guid.NewGuid();
            this.birthdate = DateTime.MinValue;
        }

        public Persona(string id, string Name, int Age, DateTime BirthDate, string photo = "",
        string Adress = "", double? Lon = null, double? Lat = null, Guid? ParentId = null, Guid? PartnerId = null, bool Exclude = false)
        {
            this.id = Guid.NewGuid();
            this.ownId = id;
            this.name = Name;
            this.age = Age;
            this.birthdate = BirthDate;
            this.photoFileName = photo ?? "";
            this.addresPlusCode = Adress;
            this.lon = Lon;
            this.lat = Lat;
            this.parentId = ParentId;
            this.partnerId = PartnerId;
            this.excludeFromDistance = Exclude;
        }
        public Persona(Guid id, string Name, int Age, DateTime BirthDate, string photo = "",
        string Adress = "", double? Lon = null, double? Lat = null, Guid? ParentId = null, Guid? PartnerId = null, bool Exclude = false)
        {
            this.id = id;
            this.name = Name;
            this.age = Age;
            this.birthdate = BirthDate;
            this.photoFileName = photo ?? "";
            this.addresPlusCode = Adress;
            this.lon = Lon;
            this.lat = Lat;
            this.parentId = ParentId;
            this.partnerId = PartnerId;
            this.excludeFromDistance = Exclude;
        }
        [JsonInclude]
        public Guid id { get; private set; }
        private string _ownId = "";
        public string ownId
        {
            get => _ownId;
            set => SetProperty(ref _ownId, value);
        }

        private string _name;
        public string name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private int _age;
        public int age
        {
            get => _age;
            set => SetProperty(ref _age, value); 
        }
        private DateTime _birthdate;
        public DateTime birthdate
        {
            get => _birthdate;
            set => SetProperty(ref _birthdate, value); 
        }
        private Guid? _parentId;
        public Guid? parentId
        {
            get => _parentId;
            set => SetProperty(ref _parentId, value);
        }
        private Guid? _partnerId;
        public Guid? partnerId
        {
            get => _partnerId;
            set => SetProperty(ref _partnerId, value);
        }
        private bool _excludeFromDistance;
        public bool excludeFromDistance
        {
            get => _excludeFromDistance;
            set => SetProperty(ref _excludeFromDistance, value);
        }
        private string _urlImage = "";
        public string photoFileName
        {
            get => _urlImage;
            set => SetProperty(ref _urlImage, value);
        }

        private string _addressPlusCode = "";
        public string addresPlusCode
        {
            get => _addressPlusCode;
            set => SetProperty(ref _addressPlusCode, value);
        }

        private double? _lon;
        public double? lon
        {
            get => _lon;
            set => SetProperty(ref _lon, value);
        }

        private double? _lat;
        public double? lat
        {
            get => _lat;
            set =>  SetProperty(ref _lat, value); 
        }

        public int CalcAge()
        {
            var today = DateTime.Today;
            int Age = today.Year - birthdate.Year;

            if (birthdate.Date > today.AddYears(-Age)) Age--;
            return Age;
        }

        public bool HasCoordinates() => lon.HasValue && lat.HasValue;

        public bool EnsureCoordinatesFromPlusCode(CalcDistance calc)
        {
            if (HasCoordinates()) return true;
            if (string.IsNullOrWhiteSpace(addresPlusCode)) return false;
            if (calc.TryConvertPlusCode(addresPlusCode, out double Lon, out double Lat))
            {
                lon = Lon;
                lat = Lat;
                return true;
            }
            return false;
        }



        public override string ToString() => $"{name} (id={ownId})";
    }


}

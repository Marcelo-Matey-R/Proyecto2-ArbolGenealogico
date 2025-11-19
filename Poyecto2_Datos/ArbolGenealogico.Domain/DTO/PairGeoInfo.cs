using System.Globalization;

namespace ArbolGenealogico.Domain.Dto
{
    public class PairGeoInfo
    {
        public Guid AId { get; set; }
        public Guid BId { get; set; }
        public string AName { get; set; } = "";
        public string BName { get; set; } = "";
        public double DistanceKm { get; set; } = double.NaN; // distancia geodésica (km)
    }
}

using System.Globalization;
using Mapsui.Projections;
using Mapsui.Extensions;
using Google.OpenLocationCode;
using ArbolGenealogico.Domain.Contracts;

namespace ArbolGenealogico.Infraestructure.Services
{
    public class CalcDistance:ICalcDistance
    {
        //convierte plus code a Lon y Lat
        public bool TryConvertPlusCode(string plus, out double Lon, out double Lat)
        {
            Lon = Lat = 0;
            if (string.IsNullOrWhiteSpace(plus)) return false;
            try
            {
                CodeArea area = OpenLocationCode.Decode(plus);
                Lon = area.CenterLongitude;
                Lat = area.CenterLatitude;
                return true;
            }
            catch
            {
                return false;
            }
        }
        //devuelve la distancia en km, ussando la conversion de SphericalMarcator
        public double Distance(double? lon1, double? lat1, double? lon2, double? lat2)
        {
            if (!lon1.HasValue || !lon2.HasValue || !lat1.HasValue || !lat2.HasValue)
            {
                return double.NaN;
            }
            var (x1, y1) = SphericalMercator.FromLonLat((double)lon1, (double)lat1);
            var (x2, y2) = SphericalMercator.FromLonLat((double)lon2, (double)lat2);

            var dx = x1 - x2;
            var dy = y1 - y2;

            var dis = Math.Sqrt(dx * dx + dy * dy);

            return (double)dis / 1000;
        }
        
        public bool TryDistanceIn(double? lon1, double? lat1, double? lon2, double? lat2, out double kiloMeters)
        {
            kiloMeters = Distance(lon1, lat1, lon2, lat2);
            return !(double.IsNaN(kiloMeters) || double.IsInfinity(kiloMeters));
        }
    }

}
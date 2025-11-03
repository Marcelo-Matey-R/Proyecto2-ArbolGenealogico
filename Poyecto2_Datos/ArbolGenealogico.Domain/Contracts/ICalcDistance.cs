using System.Globalization;

namespace ArbolGenealogico.Domain.Contracts
{
    public interface ICalcDistance
    {
        bool TryConvertPlusCode(string plus, out double lon, out double lat);
        double Distance(double? lon1, double? lat1, double? lon2, double? lat2);
        bool TryDistanceIn(double? lon1, double? lat1, double? lon2, double? lat2, out double kilometers);
    }
}
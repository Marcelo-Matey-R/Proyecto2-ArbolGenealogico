using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArbolGenealogico.Domain.DTO
{
    public class PairDistanceDto
    {
        public string PersonA { get; set; } = "";
        public string PersonB { get; set; } = "";
        public string DistanceKm { get; set; } = "";
        public string DistanceMeters { get; set; } = "";

        // Distancia numérica para ordenar/filtrar internamente
        public double DistanceNumeric { get; set; } = double.NaN;
    }
}

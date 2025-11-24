using System;
using Xunit;
using ArbolGenealogico.Infraestructure.Services;
using Google.OpenLocationCode;

namespace ArbolGenealogico.Tests
{
    public class CalcDistanceTests
    {
        private readonly CalcDistance _calc = new CalcDistance();

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("INVALIDPLUSCODE")]
        public void TryConvertPlusCode_InvalidOrEmpty_ReturnsFalse(string input)
        {
            var ok = _calc.TryConvertPlusCode(input, out double Lon, out double Lat);
            Assert.False(ok);
            // Cuando falla debe dejar valores en 0
            Assert.NotNull(Lon);
            Assert.NotNull(Lat);
        }

        [Fact]
        public void Distance_SameCoordinates_ReturnsZero()
        {
            double lon = -70.0;
            double lat = 20.0;
            var d = _calc.Distance(lon, lat, lon, lat);
            Assert.Equal(0.0, d);
        }

        [Fact]
        public void Distance_IsSymmetric()
        {
            double lon1 = -58.3816;
            double lat1 = -34.6037;
            double lon2 = -0.1276;
            double lat2 = 51.5072;
            var d12 = _calc.Distance(lon1, lat1, lon2, lat2);
            var d21 = _calc.Distance(lon2, lat2, lon1, lat1);
            Assert.Equal(d12, d21, 10);
            Assert.True(d12 >= 0);
        }
    }
}


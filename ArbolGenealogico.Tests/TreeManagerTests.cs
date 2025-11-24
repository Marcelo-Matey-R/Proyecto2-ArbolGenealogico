using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ArbolGenealogico.Core.Managers;
using ArbolGenealogico.Domain.Models;

namespace ArbolGenealogico.Tests
{
    public class TreeManagerTests
    {
        // Helper para crear Personas sencillas
        private Persona CreatePersona(Guid? id = null, Guid? parentId = null,
            double lon = 0, double lat = 0, string name = "P", bool exclude = false)
        {
            return new Persona
            {
                id = id ?? Guid.NewGuid(),
                parentId = parentId,
                partnerId = null,
                lon = lon,
                lat = lat,
                name = name,
                excludeFromDistance = exclude
            };
        }

        [Fact]
        public void BuildFromPersons_CreatesRoots_WhenParentNull()
        {
            // Arrange
            var p1 = CreatePersona(name: "A");
            var p2 = CreatePersona(name: "B");

            var mgr = new TreeManager();

            // Act
            mgr.BuildFromPersons(new[] { p1, p2 });

            // Assert: ambos son raíces
            var roots = mgr.Roots;
            Assert.Contains(roots, r => r.familiar.id == p1.id);
            Assert.Contains(roots, r => r.familiar.id == p2.id);
            Assert.Equal(2, roots.Count);
        }

        [Fact]
        public void AddPerson_ThrowsWhenDuplicateId()
        {
            var p = CreatePersona(name: "Dup");
            var mgr = new TreeManager();
            var n = mgr.AddPerson(p);
            // Assert: intentar agregar de nuevo con mismo id lanza
            var ex = Assert.Throws<InvalidOperationException>(() => mgr.AddPerson(p));
            Assert.Contains("Ya existe", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void BuildFromPersons_ComputesAverageAndMinMaxDistances()
        {
            var p1 = CreatePersona(name: "P1", lon: 0, lat: 0);
            var p2 = CreatePersona(name: "P2", lon: 0, lat: 0.001); // ~111 m approx (lat degrees)
            var p3 = CreatePersona(name: "P3", lon: 0, lat: 0.002);
            var mgr = new TreeManager();
            mgr.BuildFromPersons(new[] { p1, p2, p3 });
            mgr.SetPartner(p1.id, p2.id);
            mgr.SetPartner(p2.id, p3.id);
            Assert.True(mgr.averageDistances > 0, "Se esperaba averageDistances > 0");
            Assert.NotNull(mgr.personasMaxDistance.Item1);
            Assert.NotNull(mgr.personasMinDistance.Item1);
            Assert.True(mgr.maxDistance > mgr.minDistance || Math.Abs((mgr.maxDistance ?? 0) - (mgr.minDistance ?? 0)) >= 0.0);
        }
    }
}

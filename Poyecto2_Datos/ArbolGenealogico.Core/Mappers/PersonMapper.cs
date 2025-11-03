using System.Globalization;
using ArbolGenealogico.Domain.Contracts;
using ArbolGenealogico.Domain.Dto;
using ArbolGenealogico.Domain.Models;

namespace ArbolGenealogico.Core.Mappers
{
    public class PersonMapper : IPersonMapper
    {
        public Persona FromDto(PersonDto d)
        {
            // Ajusta los parámetros del constructor de Persona según tu implementación.
            // Aquí se asume que Persona tiene un constructor que acepta id (Guid).
            return new Persona(
                id: d.Id,
                Name: d.Nombre ?? string.Empty,
                Age: 0,
                BirthDate: DateTime.MinValue,
                photo: d.PhotoFileName ?? string.Empty,
                Addres: string.Empty,
                Lon: d.Longitude,
                Lat: d.Latitude,
                ParentId: d.ParentId,
                PartnerId: d.PartnerId,
                Exclude: d.ExcludeFromDistance
            );
        }

        public PersonDto ToDto(Persona p)
        {
            return new PersonDto
            {
                Id = p.id,
                Nombre = p.name,
                ParentId = p.parentId,
                PartnerId = p.partnerId,
                Latitude = p.lat,
                Longitude = p.lon,
                PhotoFileName = p.photoFileName,
                ExcludeFromDistance = p.excludeFromDistance
            };
        }
    }
}
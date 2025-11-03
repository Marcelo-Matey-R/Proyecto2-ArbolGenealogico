using System.Globalization;
using ArbolGenealogico.Domain.Models;
using ArbolGenealogico.Domain.Dto;

namespace ArbolGenealogico.Domain.Contracts
{
     public interface IPersonMapper
    {
        Persona FromDto(PersonDto dto);
        PersonDto ToDto(Persona p);
    }
}
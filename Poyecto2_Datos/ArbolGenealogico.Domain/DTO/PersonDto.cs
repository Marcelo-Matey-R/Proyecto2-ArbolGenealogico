using System.Globalization;

namespace ArbolGenealogico.Domain.Dto
{
    public class PersonDto
    {
        public Guid Id { get; set; }
        public string? Nombre { get; set; }
        public Guid? ParentId { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? PhotoFileName { get; set; }
        public Guid? PartnerId { get; set; }
        public bool ExcludeFromDistance { get; set; }
    }
}
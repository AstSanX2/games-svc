using Application.DTO.Bases;
using Domain.Entities;
using MongoDB.Bson;
using System.Linq.Expressions;

namespace Application.DTO.GameDTO
{
    public class FilterGameDTO : BaseFilterDTO<Game>
    {
        public ObjectId _id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public DateTime ReleaseDate { get; set; }
        public DateTime? LastUpdateDate { get; set; }
        public decimal Price { get; set; }

        public override Expression<Func<Game, bool>> GetFilterExpression()
        {
            return x => x._id == _id;
        }
    }
}

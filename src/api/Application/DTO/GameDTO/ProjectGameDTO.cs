using Application.DTO.Bases;
using Domain.Entities;
using MongoDB.Bson;
using System.Linq.Expressions;

namespace Application.DTO.GameDTO
{
    public class ProjectGameDTO : BaseProjectDTO<Game, ProjectGameDTO>
    {
        public ObjectId _id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public DateTime ReleaseDate { get; set; }
        public DateTime? LastUpdateDate { get; set; }
        public decimal Price { get; set; }

        public ProjectGameDTO(ObjectId id, string name, string description, string category, DateTime releaseDate, DateTime? lastUpdateDate, decimal price)
        {
            _id = id;
            Name = name;
            Description = description;
            Category = category;
            ReleaseDate = releaseDate;
            LastUpdateDate = lastUpdateDate;
            Price = price;
        }

        public ProjectGameDTO(Game game)
        {
            _id = game._id;
            Name = game.Name;
            Description = game.Description;
            Category = game.Category;
            ReleaseDate = game.ReleaseDate;
            LastUpdateDate = game.LastUpdateDate;
            Price = game.Price;
        }

        public ProjectGameDTO()
        {

        }

        public override Expression<Func<Game, ProjectGameDTO>> ProjectExpression()
        {
            return x => new ProjectGameDTO
            {
                _id = x._id,
                Name = x.Name,
                Description = x.Description,
                Category = x.Category,
                ReleaseDate = x.ReleaseDate,
                LastUpdateDate = x.LastUpdateDate,
                Price = x.Price
            };
        }
    }
}

using Domain.Entities;
using MongoDB.Driver;

namespace Application.DTO.Bases
{
    public abstract class BaseUpdateDTO<TEntity> where TEntity : BaseEntity
    {
        public abstract UpdateDefinition<TEntity> GetUpdateDefinition();

    }
}

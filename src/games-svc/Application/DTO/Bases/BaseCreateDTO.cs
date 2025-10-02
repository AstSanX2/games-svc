using Domain.Entities;

namespace Application.DTO.Bases
{
    public abstract class BaseCreateDTO<TEntity> where TEntity : BaseEntity
    {
        public abstract TEntity ToEntity();

    }
}

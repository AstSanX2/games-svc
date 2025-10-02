using Domain.Entities;
using System.Linq.Expressions;

namespace Application.DTO.Bases.Interfaces
{
    public interface IFilterDTO<TEntity> where TEntity : BaseEntity
    {
        Expression<Func<TEntity, bool>> GetFilterExpression();
    }
}

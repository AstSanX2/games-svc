using Domain.Entities;
using System.Linq.Expressions;

namespace Application.DTO.Bases.Interfaces
{
    public interface IProjectable<TEntity, TDTO> where TEntity : BaseEntity
    {
        Expression<Func<TEntity, TDTO>> ProjectExpression();
    }
}

using Application.DTO.Bases.Interfaces;
using Domain.Entities;
using System.Linq.Expressions;

namespace Application.DTO.Bases
{
    public abstract class BaseProjectDTO<TEntity, TProject> : IProjectable<TEntity, TProject>
        where TEntity : BaseEntity
    {
        public abstract Expression<Func<TEntity, TProject>> ProjectExpression();
    }
}

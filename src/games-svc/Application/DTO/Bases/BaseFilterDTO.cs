using Application.DTO.Bases.Interfaces;
using Domain.Entities;
using MongoDB.Bson;
using System.Linq.Expressions;

namespace Application.DTO.Bases
{
    public abstract class BaseFilterDTO<TEntity> : IFilterDTO<TEntity> where TEntity : BaseEntity
    {
        public virtual Expression<Func<TEntity, bool>> GetFilterExpression(ObjectId id)
        {
            return x => x._id == id;
        }

        public abstract Expression<Func<TEntity, bool>> GetFilterExpression();
    }
}

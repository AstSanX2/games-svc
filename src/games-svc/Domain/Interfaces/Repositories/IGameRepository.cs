using Application.DTO.GameDTO;
using Domain.Entities;
using MongoDB.Bson;

namespace Domain.Interfaces.Repositories
{
    public interface IGameRepository : IBaseRepository<Game>
    {
        // Busca avançada (Atlas Search)
        Task<IReadOnlyList<ProjectGameSearchDTO>> SearchAtlasAsync(SearchGameDTO query);

        Task<List<ProjectGameDTO>> RecommendBySimilarAsync(IReadOnlyCollection<ObjectId> likeGameIds,
                                                           IReadOnlyCollection<ObjectId> excludeGameIds,
                                                           int limit = 10);
    }
}

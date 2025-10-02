using Application.DTO.GameDTO;
using Domain.Entities;

namespace Domain.Interfaces.Repositories
{
    public interface IGameRepository : IBaseRepository<Game>
    {
        // Busca avançada (Atlas Search)
        Task<IReadOnlyList<ProjectGameSearchDTO>> SearchAtlasAsync(SearchGameDTO query);

        // Agregação de métricas (mais populares)
        Task<IReadOnlyList<PopularGameDTO>> GetPopularAsync(int top);
    }
}

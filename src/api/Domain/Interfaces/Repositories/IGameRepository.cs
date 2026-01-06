using Domain.Entities;

namespace Domain.Interfaces.Repositories
{
    public interface IGameRepository : IBaseRepository<Game>
    {
        // Métodos de busca avançada foram movidos para IGameSearchProvider
    }
}

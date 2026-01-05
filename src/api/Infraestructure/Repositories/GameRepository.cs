using Domain.Entities;
using Domain.Interfaces.Repositories;
using MongoDB.Driver;

namespace Infraestructure.Repositories
{
    public class GameRepository(IMongoDatabase database) : BaseRepository<Game>(database), IGameRepository
    {
        // Métodos de busca avançada (SearchAtlasAsync e RecommendBySimilarAsync) 
        // foram movidos para IGameSearchProvider (ElasticGameSearchProvider / FallbackMongoGameSearchProvider)
    }
}

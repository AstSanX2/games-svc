using Application.DTO.GameDTO;
using Domain.Entities;
using Domain.Interfaces.Search;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Infraestructure.Search
{
    /// <summary>
    /// Fallback para busca/recomendações quando Elasticsearch não está configurado.
    /// Usa queries simples no MongoDB (sem full-text search avançado).
    /// </summary>
    public class FallbackMongoGameSearchProvider : IGameSearchProvider
    {
        private readonly IMongoDatabase _database;
        private readonly ILogger<FallbackMongoGameSearchProvider> _logger;

        public bool IsEnabled => true; // Sempre disponível como fallback

        public FallbackMongoGameSearchProvider(IMongoDatabase database, ILogger<FallbackMongoGameSearchProvider> logger)
        {
            _database = database;
            _logger = logger;
            _logger.LogInformation("Using MongoDB fallback for game search (Elasticsearch not configured)");
        }

        public async Task<IReadOnlyList<ProjectGameSearchDTO>> SearchAsync(SearchGameDTO query, CancellationToken ct = default)
        {
            try
            {
                var collection = _database.GetCollection<Game>(nameof(Game));
                var filters = new List<FilterDefinition<Game>>();

                if (!string.IsNullOrWhiteSpace(query.Q))
                {
                    // Regex case-insensitive para busca simples
                    var regex = new BsonRegularExpression(query.Q, "i");
                    filters.Add(Builders<Game>.Filter.Or(
                        Builders<Game>.Filter.Regex(g => g.Name, regex),
                        Builders<Game>.Filter.Regex(g => g.Description, regex),
                        Builders<Game>.Filter.Regex(g => g.Category, regex)
                    ));
                }

                if (!string.IsNullOrWhiteSpace(query.Category))
                {
                    var catRegex = new BsonRegularExpression(query.Category, "i");
                    filters.Add(Builders<Game>.Filter.Regex(g => g.Category, catRegex));
                }

                var filter = filters.Count > 0
                    ? Builders<Game>.Filter.And(filters)
                    : Builders<Game>.Filter.Empty;

                var page = Math.Max(1, query.Page);
                var size = Math.Clamp(query.PageSize, 1, 100);

                var games = await collection
                    .Find(filter)
                    .SortBy(g => g.Price)
                    .Skip((page - 1) * size)
                    .Limit(size)
                    .ToListAsync(ct);

                return games.Select(g => new ProjectGameSearchDTO
                {
                    Id = g._id.ToString(),
                    Name = g.Name,
                    Category = g.Category,
                    Price = g.Price,
                    Score = 1.0 // Score fixo no fallback
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching games in MongoDB fallback");
                return Array.Empty<ProjectGameSearchDTO>();
            }
        }

        public async Task<IReadOnlyList<ProjectGameDTO>> RecommendAsync(
            IReadOnlyCollection<ObjectId> likeGameIds,
            IReadOnlyCollection<ObjectId> excludeGameIds,
            int limit = 10,
            CancellationToken ct = default)
        {
            if (likeGameIds.Count == 0)
                return Array.Empty<ProjectGameDTO>();

            try
            {
                var collection = _database.GetCollection<Game>(nameof(Game));

                // Buscar categorias dos jogos "like" para recomendar jogos da mesma categoria
                var likedGames = await collection
                    .Find(Builders<Game>.Filter.In(g => g._id, likeGameIds))
                    .Project(g => new { g.Category })
                    .ToListAsync(ct);

                var categories = likedGames.Select(g => g.Category).Distinct().ToList();

                if (categories.Count == 0)
                    return Array.Empty<ProjectGameDTO>();

                // Buscar jogos das mesmas categorias, excluindo os já comprados
                var filter = Builders<Game>.Filter.And(
                    Builders<Game>.Filter.In(g => g.Category, categories),
                    Builders<Game>.Filter.Nin(g => g._id, excludeGameIds)
                );

                var recommendations = await collection
                    .Find(filter)
                    .Limit(limit)
                    .ToListAsync(ct);

                return recommendations.Select(g => new ProjectGameDTO
                {
                    _id = g._id,
                    Name = g.Name,
                    Description = g.Description,
                    Category = g.Category,
                    Price = g.Price
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recommendations from MongoDB fallback");
                return Array.Empty<ProjectGameDTO>();
            }
        }

        public Task UpsertAsync(ProjectGameDTO game, CancellationToken ct = default)
        {
            // No-op: MongoDB já é a fonte de dados primária
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string gameId, CancellationToken ct = default)
        {
            // No-op: MongoDB já é a fonte de dados primária
            return Task.CompletedTask;
        }
    }
}


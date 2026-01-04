using Application.DTO.GameDTO;
using Domain.Interfaces.Search;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Elastic.Transport;
using Infraestructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;

namespace Infraestructure.Search
{
    /// <summary>
    /// Implementação de busca/recomendações usando Elasticsearch gerenciado.
    /// </summary>
    public class ElasticGameSearchProvider : IGameSearchProvider
    {
        private readonly ElasticsearchClient? _client;
        private readonly ElasticOptions _options;
        private readonly ILogger<ElasticGameSearchProvider> _logger;

        public bool IsEnabled => _options.IsConfigured && _client != null;

        public ElasticGameSearchProvider(IOptions<ElasticOptions> options, ILogger<ElasticGameSearchProvider> logger)
        {
            _options = options.Value;
            _logger = logger;

            if (_options.IsConfigured)
            {
                try
                {
                    _client = CreateClient(_options);
                    _logger.LogInformation("Elasticsearch client initialized for {Url}", _options.Url);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize Elasticsearch client");
                    _client = null;
                }
            }
            else
            {
                _logger.LogWarning("Elasticsearch not configured, search provider will be disabled");
            }
        }

        private static ElasticsearchClient CreateClient(ElasticOptions options)
        {
            var settings = new ElasticsearchClientSettings(new Uri(options.Url!));

            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                settings = settings.Authentication(new ApiKey(options.ApiKey));
            }
            else if (!string.IsNullOrWhiteSpace(options.Username) && !string.IsNullOrWhiteSpace(options.Password))
            {
                settings = settings.Authentication(new BasicAuthentication(options.Username, options.Password));
            }

            settings = settings
                .DefaultIndex(options.IndexName)
                .EnableDebugMode(false)
                .RequestTimeout(TimeSpan.FromSeconds(30));

            return new ElasticsearchClient(settings);
        }

        public async Task<IReadOnlyList<ProjectGameSearchDTO>> SearchAsync(SearchGameDTO query, CancellationToken ct = default)
        {
            if (_client == null)
                return Array.Empty<ProjectGameSearchDTO>();

            try
            {
                var mustQueries = new List<Query>();

                if (!string.IsNullOrWhiteSpace(query.Q))
                {
                    mustQueries.Add(new MultiMatchQuery
                    {
                        Query = query.Q,
                        Fields = new[] { "name^3", "description", "category^2" },
                        Type = TextQueryType.BestFields,
                        Fuzziness = new Fuzziness("AUTO")
                    });
                }

                if (!string.IsNullOrWhiteSpace(query.Category))
                {
                    mustQueries.Add(new MatchQuery("category")
                    {
                        Query = query.Category
                    });
                }

                var page = Math.Max(1, query.Page);
                var size = Math.Clamp(query.PageSize, 1, 100);

                var response = await _client.SearchAsync<GameDocument>(s => s
                    .Index(_options.IndexName)
                    .From((page - 1) * size)
                    .Size(size)
                    .Query(q => mustQueries.Count > 0
                        ? q.Bool(b => b.Must(mustQueries.ToArray()))
                        : q.MatchAll(new MatchAllQuery()))
                    .Sort(so => so
                        .Score(new ScoreSort { Order = SortOrder.Desc })
                        .Field("price", new FieldSort { Order = SortOrder.Asc })),
                    ct);

                if (!response.IsValidResponse)
                {
                    _logger.LogWarning("Elasticsearch search failed: {Error}", response.DebugInformation);
                    return Array.Empty<ProjectGameSearchDTO>();
                }

                return response.Documents.Select(d => new ProjectGameSearchDTO
                {
                    Id = d.Id,
                    Name = d.Name,
                    Category = d.Category,
                    Price = d.Price,
                    Score = response.Hits.FirstOrDefault(h => h.Source?.Id == d.Id)?.Score ?? 0
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching games in Elasticsearch");
                return Array.Empty<ProjectGameSearchDTO>();
            }
        }

        public async Task<IReadOnlyList<ProjectGameDTO>> RecommendAsync(
            IReadOnlyCollection<ObjectId> likeGameIds,
            IReadOnlyCollection<ObjectId> excludeGameIds,
            int limit = 10,
            CancellationToken ct = default)
        {
            if (_client == null || likeGameIds.Count == 0)
                return Array.Empty<ProjectGameDTO>();

            try
            {
                var likeIds = likeGameIds.Select(id => id.ToString()).ToArray();
                var excludeIds = excludeGameIds.Select(id => id.ToString()).ToArray();

                // More Like This query baseado nos IDs dos jogos comprados
                var response = await _client.SearchAsync<GameDocument>(s => s
                    .Index(_options.IndexName)
                    .Size(limit)
                    .Query(q => q
                        .Bool(b => b
                            .Must(m => m
                                .MoreLikeThis(mlt => mlt
                                    .Fields(new[] { "name", "description", "category" })
                                    .Like(likeIds.Select(id => new Like(new LikeDocument
                                    {
                                        Index = _options.IndexName,
                                        Id = id
                                    })).ToArray())
                                    .MinTermFreq(1)
                                    .MinDocFreq(1)
                                    .MaxQueryTerms(25)))
                            .MustNot(mn => mn
                                .Ids(new IdsQuery { Values = new Ids(excludeIds) })))),
                    ct);

                if (!response.IsValidResponse)
                {
                    _logger.LogWarning("Elasticsearch MLT query failed: {Error}", response.DebugInformation);
                    return Array.Empty<ProjectGameDTO>();
                }

                return response.Documents.Select(d => new ProjectGameDTO
                {
                    _id = ObjectId.TryParse(d.Id, out var oid) ? oid : ObjectId.Empty,
                    Name = d.Name,
                    Description = d.Description,
                    Category = d.Category,
                    Price = d.Price
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recommendations from Elasticsearch");
                return Array.Empty<ProjectGameDTO>();
            }
        }

        public async Task UpsertAsync(ProjectGameDTO game, CancellationToken ct = default)
        {
            if (_client == null)
                return;

            try
            {
                var doc = new GameDocument
                {
                    Id = game._id.ToString(),
                    Name = game.Name ?? "",
                    Description = game.Description ?? "",
                    Category = game.Category ?? "",
                    Price = game.Price
                };

                var response = await _client.IndexAsync(doc, i => i
                    .Index(_options.IndexName)
                    .Id(doc.Id)
                    .Refresh(Refresh.False),
                    ct);

                if (!response.IsValidResponse)
                {
                    _logger.LogWarning("Failed to index game {GameId}: {Error}", game._id, response.DebugInformation);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error indexing game {GameId} in Elasticsearch", game._id);
            }
        }

        public async Task DeleteAsync(string gameId, CancellationToken ct = default)
        {
            if (_client == null)
                return;

            try
            {
                var response = await _client.DeleteAsync<GameDocument>(gameId, d => d
                    .Index(_options.IndexName)
                    .Refresh(Refresh.False),
                    ct);

                if (!response.IsValidResponse && response.Result != Result.NotFound)
                {
                    _logger.LogWarning("Failed to delete game {GameId} from index: {Error}", gameId, response.DebugInformation);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting game {GameId} from Elasticsearch", gameId);
            }
        }

        /// <summary>
        /// Documento indexado no Elasticsearch
        /// </summary>
        private class GameDocument
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
            public string Category { get; set; } = "";
            public decimal Price { get; set; }
        }
    }
}


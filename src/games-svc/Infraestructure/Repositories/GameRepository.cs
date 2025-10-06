using Application.DTO.GameDTO;
using Domain.Entities;
using Domain.Interfaces.Repositories;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Infraestructure.Repositories
{
    public class GameRepository(IMongoDatabase database) : BaseRepository<Game>(database), IGameRepository
    {

        public async Task<IReadOnlyList<ProjectGameSearchDTO>> SearchAtlasAsync(SearchGameDTO query)
        {
            var coll = database.GetCollection<BsonDocument>(nameof(Game));
            var pipeline = new List<BsonDocument>();
            var must = new BsonArray();

            if (!string.IsNullOrWhiteSpace(query.Q))
            {
                must.Add(new BsonDocument("text", new BsonDocument {
                    { "query", query.Q },
                    { "path", new BsonArray { "Name", "Description", "Category" } }
                }));
            }

            if (!string.IsNullOrWhiteSpace(query.Category))
            {
                must.Add(new BsonDocument("text", new BsonDocument {
                    { "query", query.Category },
                    { "path", "Category" }
                }));
            }

            pipeline.Add(new BsonDocument("$search",
                new BsonDocument("compound", new BsonDocument("must", must))));

            pipeline.Add(new BsonDocument("$addFields",
                new BsonDocument("score", new BsonDocument("$meta", "searchScore"))));

            pipeline.Add(new BsonDocument("$sort",
                new BsonDocument { { "score", -1 }, { "Price", 1 } }));

            var page = Math.Max(1, query.Page);
            var size = Math.Clamp(query.PageSize, 1, 100);
            pipeline.Add(new BsonDocument(name: "$skip", (page - 1) * size));
            pipeline.Add(new BsonDocument(name: "$limit", size));

            // Projeção do resultado para DTO enxuto de busca
            pipeline.Add(new BsonDocument("$project", new BsonDocument {
                { "_id", 1 },
                { "Name", 1 },
                { "Category", 1 },
                { "Price", 1 },
                { "score", 1 }
            }));

            var docs = await coll.Aggregate<BsonDocument>(pipeline).ToListAsync();
            return docs.Select(d => new ProjectGameSearchDTO
            {
                Id = d.GetValue("_id", "").ToString(),
                Name = d.GetValue("Name", "").AsString,
                Category = d.GetValue("Category", "").AsString,
                Price = (decimal)(d.GetValue("Price", 0).ToDecimal()),
                Score = d.GetValue("score", 0).ToDouble()
            }).ToList();
        }

        public async Task<List<ProjectGameDTO>> RecommendBySimilarAsync(IReadOnlyCollection<ObjectId> likeGameIds,
                                                                        IReadOnlyCollection<ObjectId> excludeGameIds,
                                                                        int limit = 10)
        {
            if (likeGameIds.Count == 0)
                return [];

            var likeDocs = new BsonArray(likeGameIds.Select(id => new BsonDocument("_id", id)));
            var exclude = new BsonArray(excludeGameIds);
            var coll = database.GetCollection<BsonDocument>(nameof(Game));

            var pipeline = new[]
            {
            // Atlas Search: moreLikeThis
            new BsonDocument("$search", new BsonDocument {
                { "index", "default" },
                { "moreLikeThis", new BsonDocument {
                    { "like", likeDocs },
                    { "options", new BsonDocument {
                        { "minTermFreq", 1 }, { "minDocFreq", 1 }
                    }}
                }}
            }),

            // Excluir jogos já comprados pelo usuário
            new BsonDocument("$match", new BsonDocument {
                { "_id", new BsonDocument("$nin", exclude) }
            }),

            // Projetar os campos do DTO + score
            new BsonDocument("$project", new BsonDocument {
                { "_id", 1 },
                { "Name", 1 },
                { "Description", 1 },
                { "Category", 1 },
                { "Price", 1 },
                { "score", new BsonDocument("$meta", "searchScore") }
            }),

            new BsonDocument("$limit", limit)
        };

            // O driver faz o binding se os nomes baterem com ProjectGameDTO
            var result = await coll.Aggregate<ProjectGameDTO>(pipeline).ToListAsync();
            return result;
        }
    }
}

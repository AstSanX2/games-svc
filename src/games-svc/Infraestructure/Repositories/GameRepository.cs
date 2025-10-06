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
            if (likeGameIds.Count == 0) return [];

            var exclude = new BsonArray(excludeGameIds);

            // 1) Buscar documentos dos jogos comprados com campos textuais
            var purchasedDocs = await database.GetCollection<Game>(nameof(Game))
                .Find(Builders<Game>.Filter.In(g => g._id, likeGameIds))
                .Project(g => new { g.Name, g.Description, g.Category })
                .ToListAsync();

            // 2) Montar array de "like" com campos textuais (N documentos)
            var likeDocs = new BsonArray(
                        purchasedDocs.Select(g => new BsonDocument
                        {
                            { "Name", g.Name ?? string.Empty },
                            { "Description", g.Description ?? string.Empty },
                            { "Category", g.Category ?? string.Empty }
                        }));

            // 3) Pipeline $search moreLikeThis (sem "options") + exclusão de já-comprados
            var coll = database.GetCollection<BsonDocument>(nameof(Game));
            var pipeline = new[]
            {
                new BsonDocument("$search", new BsonDocument {
                    { "index", "default" },
                        { "moreLikeThis", new BsonDocument {
                            { "like", likeDocs }}
                        }
                }),
                new BsonDocument("$match", new BsonDocument {
                    { "_id", new BsonDocument("$nin", exclude) }
                }),
                new BsonDocument("$project", new BsonDocument {
                    { "_id", 1 },
                    { "Name", 1 },
                    { "Description", 1 },
                    { "Category", 1 },
                    { "Price", 1 },
                }),
                new BsonDocument("$limit", limit)
            };

            var result = await coll.Aggregate<ProjectGameDTO>(pipeline).ToListAsync();
            return result;
        }
    }
}

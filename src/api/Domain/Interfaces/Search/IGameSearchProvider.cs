using Application.DTO.GameDTO;
using MongoDB.Bson;

namespace Domain.Interfaces.Search
{
    /// <summary>
    /// Abstração para busca e recomendações de jogos.
    /// Permite trocar entre Elasticsearch e fallback (Mongo simples) sem alterar a lógica de negócio.
    /// </summary>
    public interface IGameSearchProvider
    {
        /// <summary>
        /// Busca jogos por texto (nome, descrição, categoria).
        /// </summary>
        Task<IReadOnlyList<ProjectGameSearchDTO>> SearchAsync(SearchGameDTO query, CancellationToken ct = default);

        /// <summary>
        /// Recomenda jogos similares aos informados (more_like_this).
        /// </summary>
        /// <param name="likeGameIds">IDs dos jogos que servem de base para similaridade</param>
        /// <param name="excludeGameIds">IDs dos jogos a excluir (ex.: já comprados)</param>
        /// <param name="limit">Máximo de resultados</param>
        Task<IReadOnlyList<ProjectGameDTO>> RecommendAsync(
            IReadOnlyCollection<ObjectId> likeGameIds,
            IReadOnlyCollection<ObjectId> excludeGameIds,
            int limit = 10,
            CancellationToken ct = default);

        /// <summary>
        /// Indexa ou atualiza um jogo no índice de busca (best-effort).
        /// </summary>
        Task UpsertAsync(ProjectGameDTO game, CancellationToken ct = default);

        /// <summary>
        /// Remove um jogo do índice de busca (best-effort).
        /// </summary>
        Task DeleteAsync(string gameId, CancellationToken ct = default);

        /// <summary>
        /// Indica se o provider está habilitado/disponível.
        /// </summary>
        bool IsEnabled { get; }
    }
}


using Application.DTO.GameDTO;
using Domain.Interfaces.Repositories;
using Domain.Interfaces.Search;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Domain.Enums;

namespace Controllers
{
    /// <summary>
    /// Endpoints administrativos para manutenção do sistema.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public class AdminController(
        IGameRepository gameRepository,
        IGameSearchProvider searchProvider,
        ILogger<AdminController> logger) : ControllerBase
    {
        /// <summary>
        /// Reindexa todos os jogos do MongoDB no Elasticsearch.
        /// Útil após migração de backend de busca ou perda de índice.
        /// </summary>
        /// <remarks>
        /// Operação potencialmente demorada para grandes volumes de dados.
        /// Recomenda-se executar em horários de baixo uso.
        /// </remarks>
        /// <returns>Estatísticas da reindexação</returns>
        [HttpPost("reindex")]
        [ProducesResponseType(typeof(ReindexResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> ReindexGames(CancellationToken ct)
        {
            if (!searchProvider.IsEnabled)
            {
                return StatusCode(503, new { error = "Search provider is not enabled or configured" });
            }

            logger.LogInformation("Starting full reindex of games to Elasticsearch");

            var games = await gameRepository.GetAllAsync<ProjectGameDTO>();
            
            int success = 0;
            int failed = 0;
            var errors = new List<string>();

            foreach (var game in games)
            {
                try
                {
                    await searchProvider.UpsertAsync(game, ct);
                    success++;
                    
                    if (success % 100 == 0)
                    {
                        logger.LogInformation("Reindex progress: {Success} games indexed", success);
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    var errorMsg = $"Failed to index game {game._id}: {ex.Message}";
                    errors.Add(errorMsg);
                    logger.LogWarning(ex, "Failed to index game {GameId}", game._id);
                }
            }

            logger.LogInformation("Reindex completed: {Success} success, {Failed} failed out of {Total} games",
                success, failed, games.Count);

            return Ok(new ReindexResult
            {
                TotalGames = games.Count,
                Indexed = success,
                Failed = failed,
                Errors = errors.Take(10).ToList() // Limita a 10 erros no response
            });
        }

        /// <summary>
        /// Verifica o status do provider de busca (Elasticsearch).
        /// </summary>
        [HttpGet("search-status")]
        [AllowAnonymous] // Permite health check sem auth
        public IActionResult GetSearchStatus()
        {
            return Ok(new
            {
                provider = searchProvider.GetType().Name,
                enabled = searchProvider.IsEnabled,
                timestamp = DateTime.UtcNow
            });
        }
    }

    public class ReindexResult
    {
        public int TotalGames { get; set; }
        public int Indexed { get; set; }
        public int Failed { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}

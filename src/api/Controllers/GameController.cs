using Application.DTO.GameDTO;
using Domain.Enums;
using Domain.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using System.Security.Claims;

namespace Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GameController(IGameService service) : ControllerBase
    {

        // [Obrigatório: "Elasticsearch" -> consultas avançadas]
        // GET /api/Game/search?q=&page=&pageSize=&category=
        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] SearchGameDTO query)
        {
            var result = await service.SearchAsync(query);
            return Ok(result);
        }

        // [Obrigatório: "Elasticsearch" -> agregações/metrics]
        // GET /api/Game/stats/popular?top=10
        [HttpGet("stats/popular")]
        public async Task<IActionResult> Popular([FromQuery] int top = 10)
        {
            if (top < 1 || top > 100) top = 10;
            var result = await service.GetPopularAsync(top);
            return Ok(result);
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            return Ok(await service.GetAllAsync());
        }

        [HttpGet("{id:length(24)}")]
        public async Task<IActionResult> Get(ObjectId id)
        {
            var game = await service.GetByIdAsync(id);
            return game is null ? NotFound() : Ok(game);
        }

        [HttpPost]
        [Authorize(Roles = nameof(UserRole.Admin))]
        public async Task<IActionResult> Post(CreateGameDTO game)
        {
            var createdGame = await service.CreateAsync(game);
            return CreatedAtAction(nameof(Get), createdGame);
        }

        [HttpPut("{id:length(24)}")]
        [Authorize(Roles = nameof(UserRole.Admin))]
        public async Task<IActionResult> Put(string id, UpdateGameDTO game)
        {
            var existingGame = await service.GetByIdAsync(ObjectId.Parse(id));
            if (existingGame is null) return NotFound();

            await service.UpdateAsync(ObjectId.Parse(id), game);
            return NoContent();
        }

        [HttpDelete("{id:length(24)}")]
        [Authorize(Roles = nameof(UserRole.Admin))]
        public async Task<IActionResult> Delete(string id)
        {
            var existingGame = await service.GetByIdAsync(ObjectId.Parse(id));
            if (existingGame is null) return NotFound();

            await service.DeleteAsync(ObjectId.Parse(id));
            return NoContent();
        }

        [HttpGet("recommendations")]
        public async Task<IActionResult> GetRecommendations([FromQuery] int limit = 10, [FromQuery] string? userId = null)
        {

            // 1) tentar pelo token (claim "UserId" ou "sub")
            var claimUserId = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(claimUserId) && ObjectId.TryParse(claimUserId, out ObjectId uid))
            {
                var list = await service.GetRecommendationsAsync(uid, limit);
                return Ok(list);
            }

            // 2) fallback: aceitar ?userId= para testes (opcional)
            if (!string.IsNullOrWhiteSpace(userId) && ObjectId.TryParse(userId, out uid))
            {
                var list = await service.GetRecommendationsAsync(uid, limit);
                return Ok(list);
            }

            return BadRequest("Não foi possível identificar o usuário (token sem UserId e sem parâmetro userId).");
        }

        /// <summary>
        /// Inicia um jogo - publica evento GameStarted na SQS
        /// </summary>
        [HttpPost("{id:length(24)}/start")]
        [Authorize]
        public async Task<IActionResult> StartGame(string id)
        {
            var claimUserId = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(claimUserId) || !ObjectId.TryParse(claimUserId, out ObjectId userId))
                return Unauthorized("Usuário não identificado");

            if (!ObjectId.TryParse(id, out ObjectId gameId))
                return BadRequest("ID de jogo inválido");

            var result = await service.StartGameAsync(gameId, userId);
            if (result.HasError)
                return StatusCode(result.StatusCode, result.Message);

            return Ok(new { message = "Jogo iniciado", gameId = id });
        }

        /// <summary>
        /// Adiciona jogo à fila - publica evento GameQueued na SQS
        /// </summary>
        [HttpPost("{id:length(24)}/queue")]
        [Authorize]
        public async Task<IActionResult> QueueGame(string id)
        {
            var claimUserId = User.FindFirstValue("UserId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(claimUserId) || !ObjectId.TryParse(claimUserId, out ObjectId userId))
                return Unauthorized("Usuário não identificado");

            if (!ObjectId.TryParse(id, out ObjectId gameId))
                return BadRequest("ID de jogo inválido");

            var result = await service.QueueGameAsync(gameId, userId);
            if (result.HasError)
                return StatusCode(result.StatusCode, result.Message);

            return Ok(new { message = "Jogo adicionado à fila", gameId = id });
        }

        /// <summary>
        /// Recebe um array de jogos e publica N mensagens na fila (1 por item) para criação assíncrona.
        /// Responde 202 Accepted quando o payload é válido.
        /// </summary>
        [HttpPost("queue")]
        [Authorize]
        public async Task<IActionResult> QueueGames([FromBody] List<CreateGameDTO>? games, CancellationToken ct)
        {
            if (games is null)
                return BadRequest(new { error = "Body deve ser um array de jogos." });

            if (games.Count == 0)
                return BadRequest(new { error = "Envie ao menos 1 jogo." });

            var claimUserId = User.FindFirstValue("UserId")
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub");

            if (string.IsNullOrWhiteSpace(claimUserId) || !ObjectId.TryParse(claimUserId, out ObjectId userId))
                return Unauthorized("Usuário não identificado");

            var errors = new List<object>();
            for (var i = 0; i < games.Count; i++)
            {
                var validation = games[i]?.Validate();
                if (validation is not null && validation.HasError)
                {
                    errors.Add(new { index = i, errors = validation.Errors });
                }
            }

            if (errors.Count > 0)
                return BadRequest(new { error = "Payload inválido", details = errors });

            var result = await service.QueueCreateGamesAsync(games, userId, ct);
            if (result.HasError)
                return StatusCode(result.StatusCode, result.Message);

            return Accepted(new { status = "ACCEPTED" });
        }
    }
}

using Application.DTO.GameDTO;
using Domain.Enums;
using Domain.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;

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
    }
}

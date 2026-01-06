using Application.DTO.PurchaseDTO;
using Domain.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using System.Security.Claims;

namespace Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PurchasesController(IPurchaseService service) : ControllerBase
    {

        // Processo assíncrono via Outbox + SQS
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Create([FromBody] CreatePurchaseDTO body, CancellationToken ct)
        {
            if (body.GameId == default)
                return BadRequest(new { error = "GameId é obrigatório" });

            var claimUserId = User.FindFirstValue("UserId")
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub");

            if (string.IsNullOrWhiteSpace(claimUserId) || !ObjectId.TryParse(claimUserId, out var userId))
                return Unauthorized("Usuário não identificado");

            try
            {
                var purchaseId = await service.CreateAsync(body.GameId, userId, ct);
                return Accepted(new { purchaseId, status = "PENDING" });
            }
            catch (InvalidOperationException ex) when (ex.Message == "Compra já realizada!")
            {
                return BadRequest("Compra já realizada!");
            }
            catch (ArgumentException)
            {
                return NotFound(new { error = "Game não encontrado" });
            }
        }

        [HttpGet("library")]
        [Authorize]
        public async Task<IActionResult> GetLibrary([FromQuery] int max = 100, CancellationToken ct = default)
        {
            var claimUserId = User.FindFirstValue("UserId")
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub");

            if (string.IsNullOrWhiteSpace(claimUserId) || !ObjectId.TryParse(claimUserId, out var userId))
                return Unauthorized("Usuário não identificado");

            var result = await service.GetUserLibraryAsync(userId, max, ct);
            return Ok(result);
        }
    }
}

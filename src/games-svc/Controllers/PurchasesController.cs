using Application.DTO.PurchaseDTO;
using Domain.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PurchasesController(IPurchaseService service) : ControllerBase
    {

        // [Serverless (processo assíncrono + gatilho) | Arquitetura (event sourcing)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreatePurchaseDTO body, CancellationToken ct)
        {
            if (body.GameId == default)
                return BadRequest(new { error = "GameId é obrigatório" });
            if (body.UserId == default)
                return BadRequest(new { error = "UserId é obrigatório" });

            var purchaseId = await service.CreateAsync(body.GameId, body.Amount, body.UserId, ct);
            return Accepted(new { purchaseId, status = "PENDING" });
        }
    }
}

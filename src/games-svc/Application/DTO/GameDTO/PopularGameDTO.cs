using MongoDB.Bson;

namespace Application.DTO.GameDTO
{
    // Resultado da agregação de popularidade (contagem de compras por jogo)
    public class PopularGameDTO
    {
        public ObjectId GameId { get; set; } = default!;
        public int Count { get; set; }
    }
}

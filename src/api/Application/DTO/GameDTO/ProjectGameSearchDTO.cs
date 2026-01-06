namespace Application.DTO.GameDTO
{
    public class ProjectGameSearchDTO
    {
        public string Id { get; set; } = default!;
        public string Name { get; set; } = default!;
        public string Category { get; set; } = default!;
        public decimal Price { get; set; }
        public double Score { get; set; } // score do Atlas Search (ranking)
    }
}

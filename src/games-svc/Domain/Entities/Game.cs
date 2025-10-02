namespace Domain.Entities
{
    public class Game : BaseEntity
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Category { get; set; }
        public DateTime ReleaseDate { get; set; }
        public DateTime? LastUpdateDate { get; set; }
        public decimal Price { get; set; }

        public Game(string name, string description, string category, DateTime releaseDate, DateTime? lastUpdateDate, decimal price)
        {
            Name = name;
            Description = description;
            Category = category;
            ReleaseDate = releaseDate;
            LastUpdateDate = lastUpdateDate;
            Price = price;
        }

        public Game()
        {

        }
    }
}

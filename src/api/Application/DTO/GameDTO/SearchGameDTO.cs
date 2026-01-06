namespace Application.DTO.GameDTO
{
    // Entrada da busca avançada
    public class SearchGameDTO
    {
        public string? Q { get; set; }          // texto livre (Name/Description/Category)
        public string? Category { get; set; }   // filtro por categoria
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }
}

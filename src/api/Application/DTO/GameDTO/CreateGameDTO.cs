using Application.DTO.Bases;
using Application.DTO.Bases.Interfaces;
using Domain.Models.Validation;
using Domain.Entities;

namespace Application.DTO.GameDTO
{
    public class CreateGameDTO : BaseCreateDTO<Game>, IValidator
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Category { get; set; }
        public DateTime ReleaseDate { get; set; }
        public DateTime? LastUpdateDate { get; set; }
        public decimal Price { get; set; }

        public CreateGameDTO()
        {

        }

        public override Game ToEntity()
        {
            return new Game
            {
                Name = Name,
                Description = Description,
                Category = Category,
                ReleaseDate = ReleaseDate,
                LastUpdateDate = LastUpdateDate,
                Price = Price
            };
        }

        public ValidationResultModel Validate()
        {
            var response = new ValidationResultModel();

            if (string.IsNullOrWhiteSpace(Name))
                response.AddError("Nome não preenchido.");

            if (string.IsNullOrWhiteSpace(Description))
                response.AddError("Descrição não preenchida.");

            if (string.IsNullOrWhiteSpace(Category))
                response.AddError("Categoria não preenchida.");

            if (ReleaseDate == default)
                response.AddError("Data de lançamento não preenchida ou inválida.");

            if (LastUpdateDate.HasValue)
            {
                if (LastUpdateDate.Value < ReleaseDate)
                    response.AddError("A data de atualização não pode ser anterior à data de lançamento.");
            }

            if (Price < 0)
                response.AddError("O preço não pode ser negativo.");

            return response;
        }
    }
}

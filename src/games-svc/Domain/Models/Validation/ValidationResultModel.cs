namespace Domain.Models.Validation
{
    public class ValidationResultModel
    {
        public List<string> Errors { get; set; } = [];
        public string? Message { get; set; }
        public bool HasError { get; set; }

        public ValidationResultModel() { }

        public override string ToString()
        {
            if (Errors != null)
            {
                return $"Ocorreram os seguintes erros: {string.Join("\n", Errors)}";
            }

            return "Erro nâo mapeado, por favor, contacte o suporte";
        }

        public void AddError(string message)
        {
            Errors.Add(message);
            HasError = true;
        }
    }
}

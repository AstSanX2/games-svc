namespace Infraestructure.Options
{
    /// <summary>
    /// Configurações para conexão com Elasticsearch gerenciado.
    /// </summary>
    public class ElasticOptions
    {
        public const string SectionName = "Elastic";

        /// <summary>
        /// URL do cluster Elasticsearch (ex: https://my-cluster.es.region.aws.found.io:9243)
        /// </summary>
        public string? Url { get; set; }

        /// <summary>
        /// Nome de usuário para autenticação básica (opcional se usar ApiKey)
        /// </summary>
        public string? Username { get; set; }

        /// <summary>
        /// Senha para autenticação básica (opcional se usar ApiKey)
        /// </summary>
        public string? Password { get; set; }

        /// <summary>
        /// API Key para autenticação (alternativa a user/password)
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>
        /// Nome do índice para jogos (default: games)
        /// </summary>
        public string IndexName { get; set; } = "games";

        /// <summary>
        /// Habilita/desabilita integração com Elasticsearch.
        /// Se false ou Url vazio, usa fallback (Mongo simples).
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Verifica se a configuração está válida para conexão
        /// </summary>
        public bool IsConfigured =>
            Enabled &&
            !string.IsNullOrWhiteSpace(Url);
    }
}


using Helpers.Extensions;
using Domain.Entities;
using MongoDB.Driver;

namespace Infraestructure.Migration
{
    /// <summary>
    /// Serviço de "seeding" que verifica se já existe algum usuário admin.
    /// Caso não exista, insere um usuário padrão.
    /// </summary>
    public class MongoSeeder : IHostedService
    {
        private readonly ILogger<MongoSeeder> _logger;
        private readonly IMongoCollection<User> _userCollection;
        private readonly string _adminEmail;
        private readonly string _adminPassword;

        // Para evitar rodar mais de uma vez, simplesmente encerraremos após o primeiro Run.
        private bool _alreadySeeded = false;

        public MongoSeeder(IMongoDatabase database, ILogger<MongoSeeder> logger, IConfiguration configuration)
        {
            _logger = logger;
            _userCollection = database.GetCollection<User>(nameof(User));

            _adminEmail = "admin@com";
            _adminPassword ="Senha@123";

#if !DEBUG
            _adminEmail = configuration.GetValue<string>("AdminUser:Email") ?? throw new Exception("AdminUser não definido") ;
            _adminPassword = configuration.GetValue<string>("AdminUser:Password") ??  throw new Exception("AdminUser não definido") ;
#endif
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_alreadySeeded)
                    return;

                _logger.LogInformation("Iniciando MongoSeeder para verificar usuário admin...");

                // Filtro para encontrar se já existe algum usuário com perfil "Admin"
                var filtro = Builders<User>.Filter.Eq(u => u.Role, Domain.Enums.UserRole.Admin);

                // Verifica se existe
                var existeAdmin = await _userCollection.Find(filtro).AnyAsync(cancellationToken);

                if (!existeAdmin)
                {
                    _logger.LogInformation("Nenhum usuário Admin encontrado. Criando usuário admin padrão...");

                    var admin = new User
                    {
                        Name = "admin",
                        Password = _adminPassword.ToHash(),
                        Role = Domain.Enums.UserRole.Admin,
                        Email = _adminEmail
                    };

                    await _userCollection.InsertOneAsync(admin, cancellationToken: cancellationToken);

                    _logger.LogInformation("Usuário Admin inserido com sucesso.");
                }
                else
                {
                    _logger.LogInformation("Já existe ao menos um usuário Admin. Pulando criação.");
                }

                _alreadySeeded = true;
            }
            catch (Exception e)
            {
                _logger.LogError($"Erro na conexão com o MongoDB: {e.Message} \n\n stack trace: \n\n {e.StackTrace}");
            }

        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}

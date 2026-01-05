using Infraestructure.Options;

namespace Helpers.Extensions
{
    public static class ConfigurationExtensions
    {
        public static JwtOptions? GetJwtOptions(this IConfiguration configuration)
        {
            var jwtSettings = configuration.GetSection(nameof(JwtOptions)).Get<JwtOptions>();

            return jwtSettings is not null ? jwtSettings : throw new Exception("jwtSettings não definido");
        }

        public static EnvironmentOptions? GetEnviormentOptions(this IConfiguration configuration)
        {
            var environmentOptions = configuration.GetSection(nameof(EnvironmentOptions)).Get<EnvironmentOptions>();

            return environmentOptions is not null ? environmentOptions : throw new Exception("EnvironmentOptions não definido");
        }
    }
}

using Domain.Enums;
using Infraestructure.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Controllers
{

    [Route("[controller]")]
    [AllowAnonymous]
    [ApiController]
    public class VersionController : ControllerBase
    {
        private readonly EnvironmentOptions _envOptions;

        public VersionController(IOptions<EnvironmentOptions> envSettings)
        {
            _envOptions = envSettings.Value;
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Get()
        {
            return Ok($"Version: 01-08-2025 | Environment: {_envOptions.Name}");
        }

    }
}

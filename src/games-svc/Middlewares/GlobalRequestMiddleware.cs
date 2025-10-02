using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text.Json;

namespace Middlewares
{
    public class GlobalRequestMiddleware : IMiddleware
    {
        private readonly ILogger<GlobalRequestMiddleware> _logger;

        public GlobalRequestMiddleware(ILogger<GlobalRequestMiddleware> logger)
        {
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            try
            {
                _logger.LogInformation("request received: " + context.Request.Method);

                await next(context);
            }
            catch (Exception e)
            {
                _logger.LogCritical(e, e.Message);

                var httpCode = (int)HttpStatusCode.InternalServerError;

                context.Response.StatusCode = httpCode;

                string json = JsonSerializer.Serialize(new ProblemDetails()
                {
                    Status = httpCode,
                    Type = "Server Error",
                    Title = "Um erro inesperado ocorreu!",
                    Detail = e.Message,
                });

                context.Response.ContentType = "application/json";

                await context.Response.WriteAsync(json);
            }
        }
    }
}

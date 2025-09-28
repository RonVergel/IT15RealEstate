using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Linq;
using System.Threading.Tasks;

namespace RealEstateCRM.Services.Logging
{
    public class ActionAuditFilter : IAsyncActionFilter
    {
        private readonly IAppLogger _logger;
        public ActionAuditFilter(IAppLogger logger) { _logger = logger; }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var http = context.HttpContext;
            var method = http.Request.Method?.ToUpperInvariant();
            var path = http.Request.Path.Value ?? string.Empty;

            // Only log mutating requests
            var shouldLog = method != "GET";
            if (!shouldLog)
            {
                await next();
                return;
            }

            // Build lightweight context without sensitive fields
            var args = context.ActionArguments
                .ToDictionary(k => k.Key, v => Sanitize(v.Value));
            var userId = http.User?.Identity?.IsAuthenticated == true ? http.User.FindFirst("sub")?.Value ?? http.User.FindFirst("nameidentifier")?.Value : null;

            try { await _logger.LogAsync("INFO", "Action", $"{method} {path}", new { args }, userId); } catch { }

            await next();
        }

        private static object? Sanitize(object? input)
        {
            if (input == null) return null;
            // Hide common sensitive values
            var type = input.GetType();
            if (type == typeof(string)) return input;
            if (type.IsPrimitive) return input;
            // For complex objects, avoid deep serialization – just type name
            return new { type = type.Name };
        }
    }
}


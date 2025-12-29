using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace RulesApp.Api;

public class Health
{
    private readonly ILogger<Health> _logger;

    public Health(ILogger<Health> logger)
    {
        _logger = logger;
    }

    [Function("Health")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "api/health")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }
}

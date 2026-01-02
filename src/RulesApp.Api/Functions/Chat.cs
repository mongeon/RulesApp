using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using RulesApp.Api.Services;
using RulesApp.Shared;

namespace RulesApp.Api.Functions;

public class Chat
{
    private readonly ILogger<Chat> _logger;
    private readonly IChatService _chatService;

    public Chat(ILogger<Chat> logger, IChatService chatService)
    {
        _logger = logger;
        _chatService = chatService;
    }

    [Function("Chat")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/chat")] HttpRequestData req,
        FunctionContext executionContext)
    {
        _logger.LogInformation("Chat request received");

        try
        {
            // Parse request
            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var chatRequest = JsonSerializer.Deserialize<ChatRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (chatRequest == null || string.IsNullOrWhiteSpace(chatRequest.Query))
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Query is required" });
                return badRequest;
            }

            // Validate query length
            if (chatRequest.Query.Length > 500)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new { error = "Query too long (max 500 characters)" });
                return badRequest;
            }

            _logger.LogInformation(
                "Processing chat query: {Query}, Season: {Season}, Association: {Association}",
                chatRequest.Query,
                chatRequest.SeasonId ?? "current",
                chatRequest.AssociationId ?? "none"
            );

            // Process query
            var response = await _chatService.ProcessQueryAsync(chatRequest);

            _logger.LogInformation(
                "Chat response: Status={Status}, Citations={CitationCount}, Context={ContextUsed}",
                response.Status,
                response.Citations.Count,
                response.ContextUsed
            );

            // Return response
            var httpResponse = req.CreateResponse(HttpStatusCode.OK);
            await httpResponse.WriteAsJsonAsync(response);
            return httpResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chat request");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Failed to process chat request" });
            return errorResponse;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace RulesApp.Api.Functions;

public class AdminDebugQueue
{
    private readonly ILogger<AdminDebugQueue> _logger;
    private readonly QueueServiceClient _queueServiceClient;

    public AdminDebugQueue(ILogger<AdminDebugQueue> logger, QueueServiceClient queueServiceClient)
    {
        _logger = logger;
        _queueServiceClient = queueServiceClient;
    }

    [Function("AdminDebugQueueGet")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "api/admin/debug/queue")] HttpRequestData req,
        CancellationToken ct)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        string? name = query["name"];
        
        if (string.IsNullOrWhiteSpace(name))
        {
            var response = req.CreateResponse(HttpStatusCode.BadRequest);
            await response.WriteAsJsonAsync(new { error = "Missing 'name' query parameter." });
            return response;
        }

        var queue = _queueServiceClient.GetQueueClient(name);
        await queue.CreateIfNotExistsAsync(cancellationToken: ct);

        var props = await queue.GetPropertiesAsync(ct);
        var approxCount = props.Value.ApproximateMessagesCount;

        var peeked = await queue.PeekMessagesAsync(5, ct);
        var messages = peeked.Value.Select(m => m.MessageText).ToArray();

        _logger.LogInformation("Queue {QueueName}: approx {Count} messages; peeked {Peeked}", name, approxCount, messages.Length);

        var okResponse = req.CreateResponse(HttpStatusCode.OK);
        await okResponse.WriteAsJsonAsync(new
        {
            name,
            approximateMessages = approxCount,
            peekedCount = messages.Length,
            messages
        });
        return okResponse;
    }

    [Function("AdminDebugQueueReceive")]
    public async Task<HttpResponseData> Receive(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "api/admin/debug/queue/receive")] HttpRequestData req,
        CancellationToken ct)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        string? name = query["name"];
        
        var maxStr = query["max"] ?? "1";
        var max = int.TryParse(maxStr, out var maxParsed) ? Math.Clamp(maxParsed, 1, 32) : 1;

        if (string.IsNullOrWhiteSpace(name))
        {
            var response = req.CreateResponse(HttpStatusCode.BadRequest);
            await response.WriteAsJsonAsync(new { error = "Missing 'name' query parameter." });
            return response;
        }

        var queue = _queueServiceClient.GetQueueClient(name);
        await queue.CreateIfNotExistsAsync(cancellationToken: ct);

        var receiveErrors = new List<string>();
        var peekErrors = new List<string>();

        // Get properties to see actual message count
        long propsApproxCount = 0;
        try
        {
            var props = await queue.GetPropertiesAsync(cancellationToken: ct);
            propsApproxCount = props.Value.ApproximateMessagesCount;
            _logger.LogInformation("Queue {Queue} properties: approx {Count} messages", name, propsApproxCount);
        }
        catch (Exception ex)
        {
            receiveErrors.Add($"properties: {ex.Message}");
        }

        QueueMessage[] received;
        try
        {
            received = (await queue.ReceiveMessagesAsync(maxMessages: max, cancellationToken: ct)).Value;
            _logger.LogInformation("Received {Count} messages from {Queue}", received.Length, name);
        }
        catch (Exception ex)
        {
            receiveErrors.Add($"receive main: {ex.Message}");
            received = Array.Empty<QueueMessage>();
        }

        var items = new List<object>();

        foreach (var msg in received)
        {
            items.Add(new { msg.MessageId, msg.PopReceipt, text = msg.MessageText });
            // Re-expose quickly so worker can still process
            await queue.UpdateMessageAsync(msg.MessageId, msg.PopReceipt, msg.MessageText, visibilityTimeout: TimeSpan.FromSeconds(1), cancellationToken: ct);
        }

        // Peek main for diagnostics
        string[] mainPeekMessages = Array.Empty<string>();
        try
        {
            var peek = await queue.PeekMessagesAsync(5, ct);
            mainPeekMessages = peek.Value.Select(p => p.MessageText).ToArray();
        }
        catch (Exception ex)
        {
            peekErrors.Add($"peek main: {ex.Message}");
        }

        // Also peek poison queue for clues
        var poisonName = $"{name}-poison";
        var poison = _queueServiceClient.GetQueueClient(poisonName);
        await poison.CreateIfNotExistsAsync(cancellationToken: ct);

        long poisonApprox = 0;
        string[] poisonMessages = Array.Empty<string>();
        var poisonItems = new List<object>();

        try
        {
            var poisonProps = await poison.GetPropertiesAsync(cancellationToken: ct);
            poisonApprox = poisonProps.Value.ApproximateMessagesCount;
            var poisonPeek = await poison.PeekMessagesAsync(5, ct);
            poisonMessages = poisonPeek.Value.Select(p => p.MessageText).ToArray();

            try
            {
                var poisonReceived = await poison.ReceiveMessagesAsync(maxMessages: max, cancellationToken: ct);
                foreach (var pm in poisonReceived.Value)
                {
                    poisonItems.Add(new { pm.MessageId, pm.PopReceipt, text = pm.MessageText });
                    // keep it in poison; reset visibility quickly
                    await poison.UpdateMessageAsync(pm.MessageId, pm.PopReceipt, pm.MessageText, visibilityTimeout: TimeSpan.FromSeconds(1), cancellationToken: ct);
                }
            }
            catch (Exception ex)
            {
                receiveErrors.Add($"receive poison: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            peekErrors.Add($"peek poison: {ex.Message}");
        }

        _logger.LogInformation("Received {Count} messages from {QueueName}; peekMain {PeekMainCount}; poison approx {PoisonCount}", items.Count, name, mainPeekMessages.Length, poisonApprox);
        var okResponse = req.CreateResponse(HttpStatusCode.OK);
        await okResponse.WriteAsJsonAsync(new
        {
            name,
            count = items.Count,
            items,
            propertiesApproxCount = propsApproxCount,
            mainPeek = new
            {
                count = mainPeekMessages.Length,
                messages = mainPeekMessages
            },
            poison = new
            {
                name = poisonName,
                approximateMessages = poisonApprox,
                peekedCount = poisonMessages.Length,
                messages = poisonMessages,
                received = poisonItems
            },
            errors = new
            {
                receive = receiveErrors,
                peek = peekErrors
            }
        });
        return okResponse;
    }

    [Function("AdminDebugQueueClear")]
    public async Task<HttpResponseData> Clear(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "api/admin/debug/queue")] HttpRequestData req,
        CancellationToken ct)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        string? name = query["name"];
        var drop = query["drop"] != null && bool.TryParse(query["drop"], out var d) && d;
        
        if (string.IsNullOrWhiteSpace(name))
        {
            var response = req.CreateResponse(HttpStatusCode.BadRequest);
            await response.WriteAsJsonAsync(new { error = "Missing 'name' query parameter." });
            return response;
        }

        var queue = _queueServiceClient.GetQueueClient(name);
        await queue.CreateIfNotExistsAsync(cancellationToken: ct);
        await queue.ClearMessagesAsync(ct);

        var poison = _queueServiceClient.GetQueueClient($"{name}-poison");
        await poison.CreateIfNotExistsAsync(cancellationToken: ct);
        await poison.ClearMessagesAsync(ct);

        if (drop)
        {
            await queue.DeleteIfExistsAsync(cancellationToken: ct);
            await poison.DeleteIfExistsAsync(cancellationToken: ct);
            _logger.LogWarning("Deleted queues {Queue} and {Poison}", name, poison.Name);
            var dropResponse = req.CreateResponse(HttpStatusCode.OK);
            await dropResponse.WriteAsJsonAsync(new { name, poison = poison.Name, cleared = true, dropped = true });
            return dropResponse;
        }

        _logger.LogInformation("Cleared all messages in queue {QueueName} and poison {Poison}", name, poison.Name);
        var clearResponse = req.CreateResponse(HttpStatusCode.OK);
        await clearResponse.WriteAsJsonAsync(new { name, poison = poison.Name, cleared = true, dropped = false });
        return clearResponse;
    }

    [Function("AdminDebugQueueSend")]
    public async Task<HttpResponseData> Send(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "api/admin/debug/queue/send")] HttpRequestData req,
        CancellationToken ct)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        string? name = query["name"];
        var body = query["body"] ?? "test-message";
        
        if (string.IsNullOrWhiteSpace(name))
        {
            var response = req.CreateResponse(HttpStatusCode.BadRequest);
            await response.WriteAsJsonAsync(new { error = "Missing 'name' query parameter." });
            return response;
        }
        if (string.IsNullOrEmpty(body))
        {
            body = "test-message";
        }

        var queue = _queueServiceClient.GetQueueClient(name);
        await queue.CreateIfNotExistsAsync(cancellationToken: ct);
        var receipt = await queue.SendMessageAsync(body, cancellationToken: ct);

        _logger.LogInformation("Sent debug message to {Queue}: id {Id}", name, receipt.Value.MessageId);
        var okResponse = req.CreateResponse(HttpStatusCode.OK);
        await okResponse.WriteAsJsonAsync(new
        {
            name,
            messageId = receipt.Value.MessageId,
            popReceipt = receipt.Value.PopReceipt,
            expirationTime = receipt.Value.ExpirationTime,
            timeNextVisible = receipt.Value.TimeNextVisible
        });
        return okResponse;
    }
}


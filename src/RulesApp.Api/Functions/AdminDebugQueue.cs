using System;
using System.Linq;
using System.Collections.Generic;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
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
    public async Task<IActionResult> Get(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "api/admin/debug/queue")] HttpRequest req,
        CancellationToken ct)
    {
        if (!req.Query.TryGetValue("name", out var nameValue))
        {
            return new BadRequestObjectResult(new { error = "Missing 'name' query parameter." });
        }
        var name = nameValue.ToString();
        if (string.IsNullOrWhiteSpace(name))
        {
            return new BadRequestObjectResult(new { error = "Missing 'name' query parameter." });
        }

        var queue = _queueServiceClient.GetQueueClient(name);
        await queue.CreateIfNotExistsAsync(cancellationToken: ct);

        var props = await queue.GetPropertiesAsync(ct);
        var approxCount = props.Value.ApproximateMessagesCount;

        var peeked = await queue.PeekMessagesAsync(5, ct);
        var messages = peeked.Value.Select(m => m.MessageText).ToArray();

        _logger.LogInformation("Queue {QueueName}: approx {Count} messages; peeked {Peeked}", name, approxCount, messages.Length);

        return new OkObjectResult(new
        {
            name,
            approximateMessages = approxCount,
            peekedCount = messages.Length,
            messages
        });
    }

    [Function("AdminDebugQueueReceive")]
    public async Task<IActionResult> Receive(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "api/admin/debug/queue/receive")] HttpRequest req,
        CancellationToken ct)
    {
        if (!req.Query.TryGetValue("name", out var nameValue))
        {
            return new BadRequestObjectResult(new { error = "Missing 'name' query parameter." });
        }
        var name = nameValue.ToString();
        
        var maxStr = req.Query.TryGetValue("max", out var maxValue) ? maxValue.ToString() : "1";
        var max = int.TryParse(maxStr, out var maxParsed) ? Math.Clamp(maxParsed, 1, 32) : 1;

        if (string.IsNullOrWhiteSpace(name))
        {
            return new BadRequestObjectResult(new { error = "Missing 'name' query parameter." });
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
        return new OkObjectResult(new
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
    }

    [Function("AdminDebugQueueClear")]
    public async Task<IActionResult> Clear(
        [HttpTrigger(AuthorizationLevel.Function, "delete", Route = "api/admin/debug/queue")] HttpRequest req,
        CancellationToken ct)
    {
        if (!req.Query.TryGetValue("name", out var nameValue))
        {
            return new BadRequestObjectResult(new { error = "Missing 'name' query parameter." });
        }
        var name = nameValue.ToString();
        var drop = req.Query.TryGetValue("drop", out var dropValue) && bool.TryParse(dropValue, out var d) && d;
        
        if (string.IsNullOrWhiteSpace(name))
        {
            return new BadRequestObjectResult(new { error = "Missing 'name' query parameter." });
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
            return new OkObjectResult(new { name, poison = poison.Name, cleared = true, dropped = true });
        }

        _logger.LogInformation("Cleared all messages in queue {QueueName} and poison {Poison}", name, poison.Name);
        return new OkObjectResult(new { name, poison = poison.Name, cleared = true, dropped = false });
    }

    [Function("AdminDebugQueueSend")]
    public async Task<IActionResult> Send(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "api/admin/debug/queue/send")] HttpRequest req,
        CancellationToken ct)
    {
        if (!req.Query.TryGetValue("name", out var nameValue))
        {
            return new BadRequestObjectResult(new { error = "Missing 'name' query parameter." });
        }
        var name = nameValue.ToString();
        var body = req.Query.TryGetValue("body", out var bodyValue) ? bodyValue.ToString() : "test-message";
        
        if (string.IsNullOrWhiteSpace(name))
        {
            return new BadRequestObjectResult(new { error = "Missing 'name' query parameter." });
        }
        if (string.IsNullOrEmpty(body))
        {
            body = "test-message";
        }

        var queue = _queueServiceClient.GetQueueClient(name);
        await queue.CreateIfNotExistsAsync(cancellationToken: ct);
        var receipt = await queue.SendMessageAsync(body, cancellationToken: ct);

        _logger.LogInformation("Sent debug message to {Queue}: id {Id}", name, receipt.Value.MessageId);
        return new OkObjectResult(new
        {
            name,
            messageId = receipt.Value.MessageId,
            popReceipt = receipt.Value.PopReceipt,
            expirationTime = receipt.Value.ExpirationTime,
            timeNextVisible = receipt.Value.TimeNextVisible
        });
    }
}

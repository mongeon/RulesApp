using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RulesApp.Api.Functions;
using RulesApp.Api.Services;

var builder = FunctionsApplication.CreateBuilder(args);

// Azure Storage clients - prefer AzureWebJobsStorage to align all bindings (HTTP + triggers)
var storageConnectionString =
    builder.Configuration["AzureWebJobsStorage"]
    ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage")
    ?? builder.Configuration["Storage:ConnectionString"]
    ?? Environment.GetEnvironmentVariable("Storage:ConnectionString")
    ?? "UseDevelopmentStorage=true";  // Default for local development

builder.Services.AddSingleton(new BlobServiceClient(storageConnectionString));

var queueClientOptions = new QueueClientOptions
{
    MessageEncoding = QueueMessageEncoding.None  // Plain text for Functions compatibility
};
builder.Services.AddSingleton(new QueueServiceClient(storageConnectionString, queueClientOptions));

builder.Services.AddSingleton(new TableServiceClient(storageConnectionString));

// Storage wrappers
builder.Services.AddSingleton<IBlobStore, BlobStore>();
builder.Services.AddSingleton<IQueueStore, QueueStore>();
builder.Services.AddSingleton<ITableStore, TableStore>();

// Business services
builder.Services.AddSingleton<IPdfExtractor, PdfExtractor>();
builder.Services.AddSingleton<IChunker, Chunker>();
builder.Services.AddSingleton<OverrideDetector>();
builder.Services.AddSingleton<PrecedenceResolver>();

// Azure AI Search
var searchEndpoint = builder.Configuration["Search:Endpoint"]
    ?? Environment.GetEnvironmentVariable("Search:Endpoint")
    ?? throw new InvalidOperationException("Search:Endpoint not configured");
var searchAdminKey = builder.Configuration["Search:AdminKey"]
    ?? Environment.GetEnvironmentVariable("Search:AdminKey")
    ?? throw new InvalidOperationException("Search:AdminKey not configured");
var searchIndexName = builder.Configuration["Search:IndexName"]
    ?? Environment.GetEnvironmentVariable("Search:IndexName")
    ?? "rules-active";

builder.Services.AddSingleton<ISearchStore>(new SearchStore(searchEndpoint, searchAdminKey, searchIndexName));

// Azure OpenAI (required for chat AI enhancement)
var openAiEndpoint = builder.Configuration["OpenAI:Endpoint"]
    ?? Environment.GetEnvironmentVariable("OpenAI:Endpoint")
    ?? throw new InvalidOperationException("OpenAI:Endpoint not configured");
var openAiKey = builder.Configuration["OpenAI:Key"]
    ?? Environment.GetEnvironmentVariable("OpenAI:Key")
    ?? throw new InvalidOperationException("OpenAI:Key not configured");
var openAiDeploymentName = builder.Configuration["OpenAI:DeploymentName"]
    ?? Environment.GetEnvironmentVariable("OpenAI:DeploymentName")
    ?? "gpt-4o-mini";

builder.Services.AddSingleton<IChatService>(sp =>
    new ChatService(
        sp.GetRequiredService<ISearchStore>(),
        sp.GetRequiredService<PrecedenceResolver>(),
        openAiEndpoint,
        openAiKey,
        openAiDeploymentName
    ));

// Worker (for manual invocation testing)
builder.Services.AddSingleton<RulesIngestWorker>();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Build().Run();

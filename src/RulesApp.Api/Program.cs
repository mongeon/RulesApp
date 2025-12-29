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

builder.ConfigureFunctionsWebApplication();

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

// Worker (for manual invocation testing)
builder.Services.AddSingleton<RulesIngestWorker>();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Build().Run();

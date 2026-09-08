using System.Text.Json.Serialization;
using FolderCompare.Api.Configuration;
using FolderCompare.Api.Infrastructure;
using FolderCompare.Api.Models;
using FolderCompare.Api.Services;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ComparisonOptions>(builder.Configuration.GetSection(ComparisonOptions.SectionName));

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// Model binding failures are reported through the same structured error contract as everything else.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var message = context.ModelState
            .SelectMany(entry => entry.Value?.Errors.Select(error => error.ErrorMessage) ?? Array.Empty<string>())
            .FirstOrDefault() ?? "The request is not valid.";

        return new BadRequestObjectResult(new ApiError(ErrorCodes.ValidationFailed, message));
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

builder.Services.AddSingleton<IArchiveScanner, ZipArchiveScanner>();
builder.Services.AddSingleton<IArchiveTreeService, ArchiveTreeService>();
builder.Services.AddSingleton<IFolderScanner, FolderScanner>();
builder.Services.AddSingleton<IFileComparisonService, FileComparisonService>();
builder.Services.AddSingleton<IComparisonTreeBuilder, ComparisonTreeBuilder>();
builder.Services.AddSingleton<IComparisonStore, InMemoryComparisonStore>();
builder.Services.AddSingleton<IComparisonService, ComparisonService>();
builder.Services.AddSingleton<IFileContentService, FileContentService>();

const string CorsPolicy = "frontend";
var corsOrigins = builder.Configuration
    .GetSection($"{ComparisonOptions.SectionName}:CorsOrigins")
    .Get<string[]>() ?? new[] { "http://localhost:4200" };

builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseMiddleware<ErrorHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(CorsPolicy);

// The published Angular build is copied into wwwroot, which makes the API self-hosting.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Exposed so integration tests can spin the API up with WebApplicationFactory.</summary>
public partial class Program;

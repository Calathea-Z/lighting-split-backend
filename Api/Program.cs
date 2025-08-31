using Api.Data;
using Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Api.Services;
using Api.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// EF Core + PostgreSQL
builder.Services.AddDbContext<LightningDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Controllers
builder.Services.AddControllers();

// CORS
const string CorsPolicy = "NextDev";
builder.Services.AddCors(opt =>
{
    opt.AddPolicy(CorsPolicy, p =>
        p.WithOrigins("http://localhost:3000") // frontend dev URL
         .AllowAnyHeader()
         .AllowAnyMethod());
});

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<UploadOptions>(opts =>
{
    opts.RootFolder = "uploads";
    opts.PublicRequestPath = "/uploads";
    opts.MaxBytes = 10 * 1024 * 1024;
});

var cfg = builder.Configuration;

// Handle Azure Storage connection string safely
var connectionString = cfg["AzureStorage:ConnectionString"];
if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddSingleton(new BlobServiceClient(connectionString));
    builder.Services.AddSingleton(new QueueServiceClient(connectionString));
    builder.Services.AddSingleton<IParseQueue, AzureStorageParseQueue>();
}
else
{
    // Use development storage if no connection string is provided
    var devConnectionString = "UseDevelopmentStorage=true";
    builder.Services.AddSingleton(new BlobServiceClient(devConnectionString));
    builder.Services.AddSingleton(new QueueServiceClient(devConnectionString));
    builder.Services.AddSingleton<IParseQueue, AzureStorageParseQueue>();
}

builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
builder.Services.AddScoped<IReceiptService, ReceiptService>();

var app = builder.Build();

// Serve /uploads from ContentRoot/uploads
var uploadsAbs = Path.Combine(app.Environment.ContentRootPath, "uploads");
Directory.CreateDirectory(uploadsAbs);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsAbs),
    RequestPath = "/uploads"
});

// Enable Swagger UI
app.UseSwagger();
app.UseSwaggerUI();

// Enable CORS
app.UseCors(CorsPolicy);

// Map controllers
app.MapControllers();

// Health + root
app.MapGet("/", () => "Lightning Split API");
app.MapGet("/healthz", () => Results.Ok(new { ok = true }));

app.Run();
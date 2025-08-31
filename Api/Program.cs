using Api.Data;
using Api.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

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

builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();

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

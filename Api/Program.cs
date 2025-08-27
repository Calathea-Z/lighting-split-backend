using Api.Data;
using Microsoft.EntityFrameworkCore;

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

var app = builder.Build();

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

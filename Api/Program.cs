using Api.Common.Interfaces;
using Api.Common.Services;
using Api.Data;
using Api.Infrastructure.Interfaces;
using Api.Infrastructure.Queues;
using Api.Infrastructure.Storage;
using Api.Options;
using Api.Options.Validators;
using Api.Services.Payments;
using Api.Services.Payments.Abstractions;
using Api.Services.Receipts;
using Api.Services.Receipts.Abstractions;
using Api.Services.Reconciliation;
using Api.Services.Reconciliation.Abstractions;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
var env = builder.Environment;

/* ---------- Options (typed + validated) ---------- */
builder.Services
    .AddOptions<StorageOptions>().Bind(cfg.GetSection("Storage"))
    .ValidateDataAnnotations().ValidateOnStart();

builder.Services
    .AddOptions<UploadOptions>().Bind(cfg.GetSection("Upload"))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<UploadOptions>, UploadOptionsValidator>();

// AOK security (HMAC pepper)
builder.Services
    .AddOptions<AokSecurityOptions>()
    .Bind(cfg.GetSection("AokSecurity"))
    .Validate(o =>
    {
        try
        {
            var b64 = o.PepperBase64 ?? string.Empty;
            var bytes = Convert.FromBase64String(b64);
            return bytes.Length >= 32; // require >= 256-bit key
        }
        catch
        {
            return false;
        }
    }, "AokSecurity: PepperBase64 must be Base64 and at least 32 bytes (256-bit).")
    .ValidateOnStart();

/* ---------- EF Core: PostgreSQL ---------- */
builder.Services.AddDbContext<LightningDbContext>(opts =>
    opts.UseNpgsql(cfg.GetConnectionString("Default")));

/* ---------- MVC ---------- */
builder.Services.AddControllers();
builder.Services.AddProblemDetails();

/* ---------- CORS ---------- */
const string CorsPolicy = "UiDev";
builder.Services.AddCors(opt =>
{
    var origins = cfg.GetSection("Cors:Origins").Get<string[]>() ?? new[] { "http://localhost:3000" };
    opt.AddPolicy(CorsPolicy, p => p
        .WithOrigins(origins)
        .AllowAnyHeader()
        .AllowAnyMethod());
});

/* ---------- Swagger ---------- */
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

/* ---------- Azure Storage (Blob + Queue) ---------- */
string? storageConn =
    cfg.GetConnectionString("AzureStorage")
    ?? cfg["AzureStorage:ConnectionString"]
    ?? (env.IsDevelopment() ? "UseDevelopmentStorage=true" : null);

if (!string.IsNullOrWhiteSpace(storageConn))
{
    builder.Services.AddSingleton(new BlobServiceClient(storageConn));
    builder.Services.AddSingleton(new QueueServiceClient(storageConn));
    builder.Services.AddSingleton<IParseQueue, AzureBlobParseQueue>();
}

/* ---------- App Services ---------- */
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
builder.Services.AddSingleton<IClock, SystemClock>();

builder.Services.AddScoped<IReceiptService, ReceiptService>();
builder.Services.AddScoped<IReceiptItemsService, ReceiptItemsService>();
builder.Services.AddScoped<IReceiptReconciliationOrchestrator, ReceiptReconciliationOrchestrator>();
builder.Services.AddScoped<IReceiptReconciliationCalculator, ReceiptReconciliationCalculator>();
builder.Services.AddScoped<IPaymentLinkBuilder, PaymentLinkBuilder>();
builder.Services.AddScoped<ISplitCalculator, SplitCalculator>();
builder.Services.AddScoped<IAokService, AokService>(); // uses AokSecurityOptions (HMAC pepper)
builder.Services.AddScoped<ISplitFinalizerService, SplitFinalizerService>();
builder.Services.AddScoped<ISplitShareReader, SplitShareReader>();
builder.Services.AddScoped<ISplitPaymentService, SplitPaymentService>();
builder.Services.AddScoped<IShareCodeService, ShareCodeService>();

var app = builder.Build();

/* ---------- Middleware pipeline ---------- */
if (env.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler();   // maps to ProblemDetails
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseCors(CorsPolicy);

app.MapControllers();

/* ---------- Static files (/uploads) ---------- */
var uploadOpts = app.Services.GetRequiredService<IOptions<UploadOptions>>().Value;
var uploadsAbs = Path.Combine(env.ContentRootPath, uploadOpts.RootFolder ?? "uploads");
Directory.CreateDirectory(uploadsAbs);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsAbs),
    RequestPath = uploadOpts.PublicRequestPath ?? "/uploads"
});

app.Run();
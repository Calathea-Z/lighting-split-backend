using Api.Abstractions.Receipts;
using Api.Data;
using Api.Models.Receipts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.UnitTests;

public static class TestConfiguration
{
    public static IServiceCollection CreateTestServices()
    {
        var services = new ServiceCollection();

        // Add in-memory database
        services.AddDbContext<LightningDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));

        return services;
    }

    public static LightningDbContext CreateTestDbContext()
    {
        var options = new DbContextOptionsBuilder<LightningDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new LightningDbContext(options);
    }

    public static async Task<LightningDbContext> CreateTestDbContextWithDataAsync()
    {
        var context = CreateTestDbContext();

        // Add some test data
        var receipts = new List<Receipt>
        {
            TestHelpers.CreateTestReceipt(status: ReceiptStatus.Parsed),
            TestHelpers.CreateTestReceipt(status: ReceiptStatus.PendingParse),
            TestHelpers.CreateTestReceipt(status: ReceiptStatus.FailedParse)
        };

        context.Receipts.AddRange(receipts);
        await context.SaveChangesAsync();

        return context;
    }
}

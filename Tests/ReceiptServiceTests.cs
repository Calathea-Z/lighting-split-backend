using Api.Data;
using Api.Dtos;
using Api.Interfaces;
using Api.Options;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Tests;

public class ReceiptServiceTests : IDisposable
{
    private readonly LightningDbContext _dbContext;
    private readonly Mock<BlobServiceClient> _mockBlobServiceClient;
    private readonly Mock<IParseQueue> _mockParseQueue;
    private readonly IOptions<StorageOptions> _storageOptions;
    private readonly ReceiptService _receiptService;

    public ReceiptServiceTests()
    {
        // In-memory DB
        var dbOpts = new DbContextOptionsBuilder<LightningDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new LightningDbContext(dbOpts);

        // Mocks
        _mockBlobServiceClient = new Mock<BlobServiceClient>();
        _mockParseQueue = new Mock<IParseQueue>();

        // Options
        _storageOptions = Options.Create(new StorageOptions
        {
            ReceiptsContainer = "receipts",
            OverwriteOnUpload = true
        });

        // SUT
        _receiptService = new ReceiptService(
            _dbContext,
            _mockBlobServiceClient.Object,
            _mockParseQueue.Object,
            _storageOptions
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task CreateAsync_WithValidDto_ShouldCreateReceipt()
    {
        // Arrange
        var dto = new CreateReceiptDto
        {
            Items = new List<CreateReceiptItemDto>
            {
                new()
                {
                    Label = "Test Item",
                    Qty = 2,
                    UnitPrice = 10.50m
                }
            }
        };

        // Act
        var result = await _receiptService.CreateAsync(dto);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBeEmpty();
        result.Status.Should().Be("Parsed");
        result.ItemCount.Should().Be(1);
        result.SubTotal.Should().Be(21.00m); // 2 * 10.50
        result.Total.Should().Be(21.00m); // No tax or tip

        // Verify database
        var savedReceipt = await _dbContext.Receipts
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == result.Id);
        
        savedReceipt.Should().NotBeNull();
        savedReceipt!.Items.Should().HaveCount(1);
        savedReceipt.Items[0].Label.Should().Be("Test Item");
        savedReceipt.Items[0].LineSubtotal.Should().Be(21.00m);
    }

    [Fact]
    public async Task CreateAsync_WithNullDto_ShouldThrowArgumentException()
    {
        // Act & Assert
        await _receiptService.Invoking(s => s.CreateAsync(null!))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Body is required*");
    }

    [Fact]
    public async Task CreateAsync_WithEmptyItems_ShouldThrowArgumentException()
    {
        // Arrange
        var dto = new CreateReceiptDto { Items = new List<CreateReceiptItemDto>() };

        // Act & Assert
        await _receiptService.Invoking(s => s.CreateAsync(dto))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*At least one item is required*");
    }

    [Fact]
    public async Task CreateAsync_WithInvalidItemQuantities_ShouldThrowArgumentException()
    {
        // Arrange
        var dto = new CreateReceiptDto
        {
            Items = new List<CreateReceiptItemDto>
            {
                new() { Label = "Test", Qty = 0, UnitPrice = 10m }
            }
        };

        // Act & Assert
        await _receiptService.Invoking(s => s.CreateAsync(dto))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*quantities must be > 0*");
    }

    [Fact]
    public async Task CreateAsync_WithNegativePrices_ShouldThrowArgumentException()
    {
        // Arrange
        var dto = new CreateReceiptDto
        {
            Items = new List<CreateReceiptItemDto>
            {
                new() { Label = "Test", Qty = 1, UnitPrice = -10m }
            }
        };

        // Act & Assert
        await _receiptService.Invoking(s => s.CreateAsync(dto))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*prices must be >= 0*");
    }

    [Fact]
    public async Task CreateAsync_WithTaxAndDiscount_ShouldCalculateCorrectly()
    {
        // Arrange
        var dto = new CreateReceiptDto
        {
            Items = new List<CreateReceiptItemDto>
            {
                new()
                {
                    Label = "Test Item",
                    Qty = 2,
                    UnitPrice = 10.00m,
                    Discount = 2.00m,
                    Tax = 1.50m
                }
            }
        };

        // Act
        var result = await _receiptService.CreateAsync(dto);

        // Assert
        result.SubTotal.Should().Be(18.00m); // (2 * 10) - 2 discount
        result.Total.Should().Be(19.50m); // 18 + 1.50 tax

        var savedReceipt = await _dbContext.Receipts
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == result.Id);
        
        savedReceipt!.Items[0].LineSubtotal.Should().Be(18.00m);
        savedReceipt.Items[0].LineTotal.Should().Be(19.50m);
    }

    [Fact]
    public async Task GetByIdAsync_WithExistingReceipt_ShouldReturnReceipt()
    {
        // Arrange
        var receipt = new Api.Models.Receipt
        {
            Id = Guid.NewGuid(),
            Status = "Parsed",
            Items = new List<Api.Models.ReceiptItem>
            {
                new() { Label = "Test Item", Qty = 1, UnitPrice = 10m }
            }
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _receiptService.GetByIdAsync(receipt.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(receipt.Id);
        result.Status.Should().Be("Parsed");
        result.Items.Should().HaveCount(1);
        result.Items[0].Label.Should().Be("Test Item");
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistentReceipt_ShouldReturnNull()
    {
        // Act
        var result = await _receiptService.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_WithNoFilter_ShouldReturnAllReceipts()
    {
        // Arrange
        var receipts = new List<Api.Models.Receipt>
        {
            new() { Id = Guid.NewGuid(), Status = "Parsed", CreatedAt = DateTimeOffset.UtcNow.AddDays(-1) },
            new() { Id = Guid.NewGuid(), Status = "Parsed", CreatedAt = DateTimeOffset.UtcNow }
        };
        _dbContext.Receipts.AddRange(receipts);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _receiptService.ListAsync(null, 0, 10);

        // Assert
        result.Should().HaveCount(2);
        result.Should().BeInDescendingOrder(r => r.CreatedAt);
    }

    [Fact]
    public async Task ListAsync_WithOwnerFilter_ShouldReturnFilteredReceipts()
    {
        // Arrange
        var receipts = new List<Api.Models.Receipt>
        {
            new() { Id = Guid.NewGuid(), OwnerUserId = "user1", Status = "Parsed" },
            new() { Id = Guid.NewGuid(), OwnerUserId = "user2", Status = "Parsed" },
            new() { Id = Guid.NewGuid(), OwnerUserId = "user1", Status = "Parsed" }
        };
        _dbContext.Receipts.AddRange(receipts);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _receiptService.ListAsync("user1", 0, 10);

        // Assert
        result.Should().HaveCount(2);
        result.Should().OnlyContain(r => r.Id == receipts[0].Id || r.Id == receipts[2].Id);
    }

    [Fact]
    public async Task ListAsync_WithPagination_ShouldRespectSkipAndTake()
    {
        // Arrange
        var receipts = new List<Api.Models.Receipt>();
        for (int i = 0; i < 5; i++)
        {
            receipts.Add(new Api.Models.Receipt
            {
                Id = Guid.NewGuid(),
                Status = "Parsed",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-i)
            });
        }
        _dbContext.Receipts.AddRange(receipts);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _receiptService.ListAsync(null, 2, 2);

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task DeleteAsync_WithExistingReceipt_ShouldDeleteReceipt()
    {
        // Arrange
        var receipt = new Api.Models.Receipt
        {
            Id = Guid.NewGuid(),
            Status = "Parsed",
            BlobContainer = "receipts",
            BlobName = "test.jpg"
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        // Mock blob deletion
        var mockContainer = new Mock<BlobContainerClient>();
        var mockBlob = new Mock<BlobClient>();
        _mockBlobServiceClient.Setup(x => x.GetBlobContainerClient("receipts"))
            .Returns(mockContainer.Object);
        mockContainer.Setup(x => x.GetBlobClient("test.jpg"))
            .Returns(mockBlob.Object);

        // Act
        var result = await _receiptService.DeleteAsync(receipt.Id);

        // Assert
        result.Should().BeTrue();
        var deletedReceipt = await _dbContext.Receipts.FindAsync(receipt.Id);
        deletedReceipt.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistentReceipt_ShouldReturnFalse()
    {
        // Act
        var result = await _receiptService.DeleteAsync(Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateTotalsAsync_WithExistingReceipt_ShouldUpdateTotals()
    {
        // Arrange
        var receipt = new Api.Models.Receipt
        {
            Id = Guid.NewGuid(),
            Status = "PendingParse",
            Items = new List<Api.Models.ReceiptItem>
            {
                new() { Label = "Test", Qty = 2, UnitPrice = 10m, LineSubtotal = 20m, LineTotal = 20m }
            }
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        var updateDto = new UpdateTotalsDto(20m, 2m, 3m, 25m);

        // Act
        var result = await _receiptService.UpdateTotalsAsync(receipt.Id, updateDto);

        // Assert
        result.Should().NotBeNull();
        result!.SubTotal.Should().Be(20m);
        result.Tax.Should().Be(2m);
        result.Tip.Should().Be(3m);
        result.Total.Should().Be(25m);
        result.Status.Should().Be("Parsed");

        var updatedReceipt = await _dbContext.Receipts.FindAsync(receipt.Id);
        updatedReceipt!.SubTotal.Should().Be(20m);
        updatedReceipt.Status.Should().Be("Parsed");
    }

    [Fact]
    public async Task UpdateTotalsAsync_WithNonExistentReceipt_ShouldReturnNull()
    {
        // Arrange
        var updateDto = new UpdateTotalsDto(20m, null, null, null);

        // Act
        var result = await _receiptService.UpdateTotalsAsync(Guid.NewGuid(), updateDto);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task MarkParseFailedAsync_WithExistingReceipt_ShouldMarkAsFailed()
    {
        // Arrange
        var receipt = new Api.Models.Receipt
        {
            Id = Guid.NewGuid(),
            Status = "PendingParse"
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _receiptService.MarkParseFailedAsync(receipt.Id, "Test error");

        // Assert
        result.Should().BeTrue();
        var failedReceipt = await _dbContext.Receipts.FindAsync(receipt.Id);
        failedReceipt!.Status.Should().Be("FailedParse");
        failedReceipt.ParseError.Should().Be("Test error");
    }

    [Fact]
    public async Task MarkParseFailedAsync_WithNonExistentReceipt_ShouldReturnFalse()
    {
        // Act
        var result = await _receiptService.MarkParseFailedAsync(Guid.NewGuid(), "Test error");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task AddItemAsync_WithValidDto_ShouldAddItem()
    {
        // Arrange
        var receipt = new Api.Models.Receipt
        {
            Id = Guid.NewGuid(),
            Status = "Parsed"
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        var itemDto = new CreateReceiptItemDto
        {
            Label = "New Item",
            Qty = 3,
            UnitPrice = 5.50m
        };

        // Act
        var result = await _receiptService.AddItemAsync(receipt.Id, itemDto);

        // Assert
        result.Should().NotBeNull();
        result!.Label.Should().Be("New Item");
        result.Qty.Should().Be(3);
        result.UnitPrice.Should().Be(5.50m);

        var updatedReceipt = await _dbContext.Receipts
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == receipt.Id);
        
        updatedReceipt!.Items.Should().HaveCount(1);
        updatedReceipt.Items[0].LineSubtotal.Should().Be(16.50m); // 3 * 5.50
    }

    [Fact]
    public async Task AddItemAsync_WithNonExistentReceipt_ShouldReturnNull()
    {
        // Arrange
        var itemDto = new CreateReceiptItemDto
        {
            Label = "Test Item",
            Qty = 1,
            UnitPrice = 10m
        };

        // Act
        var result = await _receiptService.AddItemAsync(Guid.NewGuid(), itemDto);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateItemAsync_WithValidDto_ShouldUpdateItem()
    {
        // Arrange
        var receipt = new Api.Models.Receipt
        {
            Id = Guid.NewGuid(),
            Status = "Parsed"
        };
        var item = new Api.Models.ReceiptItem
        {
            Id = Guid.NewGuid(),
            ReceiptId = receipt.Id,
            Label = "Original Item",
            Qty = 1,
            UnitPrice = 10m,
            Version = 1
        };
        _dbContext.Receipts.Add(receipt);
        _dbContext.ReceiptItems.Add(item);
        await _dbContext.SaveChangesAsync();

        var updateDto = new UpdateReceiptItemDto
        {
            Label = "Updated Item",
            Qty = 2,
            UnitPrice = 15m,
            Version = 1
        };

        // Act
        var result = await _receiptService.UpdateItemAsync(receipt.Id, item.Id, updateDto);

        // Assert
        result.Should().NotBeNull();
        result!.Label.Should().Be("Updated Item");
        result.Qty.Should().Be(2);
        result.UnitPrice.Should().Be(15m);

        var updatedItem = await _dbContext.ReceiptItems.FindAsync(item.Id);
        updatedItem!.LineSubtotal.Should().Be(30m); // 2 * 15
    }

    [Fact]
    public async Task UpdateItemAsync_WithConcurrencyConflict_ShouldThrowException()
    {
        // Arrange
        var receipt = new Api.Models.Receipt
        {
            Id = Guid.NewGuid(),
            Status = "Parsed"
        };
        var item = new Api.Models.ReceiptItem
        {
            Id = Guid.NewGuid(),
            ReceiptId = receipt.Id,
            Label = "Test Item",
            Qty = 1,
            UnitPrice = 10m,
            Version = 1
        };
        _dbContext.Receipts.Add(receipt);
        _dbContext.ReceiptItems.Add(item);
        await _dbContext.SaveChangesAsync();

        var updateDto = new UpdateReceiptItemDto
        {
            Label = "Updated Item",
            Version = 0 // Wrong version
        };

        // Act & Assert
        await _receiptService.Invoking(s => s.UpdateItemAsync(receipt.Id, item.Id, updateDto))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Concurrency conflict*");
    }

    [Fact]
    public async Task DeleteItemAsync_WithExistingItem_ShouldDeleteItem()
    {
        // Arrange
        var receipt = new Api.Models.Receipt
        {
            Id = Guid.NewGuid(),
            Status = "Parsed"
        };
        var item = new Api.Models.ReceiptItem
        {
            Id = Guid.NewGuid(),
            ReceiptId = receipt.Id,
            Label = "Test Item",
            Qty = 1,
            UnitPrice = 10m
        };
        _dbContext.Receipts.Add(receipt);
        _dbContext.ReceiptItems.Add(item);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _receiptService.DeleteItemAsync(receipt.Id, item.Id, null);

        // Assert
        result.Should().BeTrue();
        var deletedItem = await _dbContext.ReceiptItems.FindAsync(item.Id);
        deletedItem.Should().BeNull();
    }

    [Fact]
    public async Task DeleteItemAsync_WithNonExistentItem_ShouldReturnFalse()
    {
        // Act
        var result = await _receiptService.DeleteItemAsync(Guid.NewGuid(), Guid.NewGuid(), null);

        // Assert
        result.Should().BeFalse();
    }
}

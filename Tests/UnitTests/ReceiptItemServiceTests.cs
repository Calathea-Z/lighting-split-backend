using Api.Abstractions.Receipts;
using Api.Abstractions.Transport;
using Api.Common.Interfaces;
using Api.Data;
using Api.Dtos.Receipts.Requests.Items;
using Api.Models.Receipts;
using Api.Services.Receipts;
using Api.Services.Receipts.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Tests.UnitTests;

public class ReceiptItemServiceTests : IDisposable
{
    private readonly LightningDbContext _dbContext;
    private readonly Mock<IClock> _mockClock;
    private readonly Mock<IReceiptReconciliationOrchestrator> _mockReconciler;
    private readonly ReceiptItemsService _receiptItemService;

    public ReceiptItemServiceTests()
    {
        // In-memory DB
        var dbOpts = new DbContextOptionsBuilder<LightningDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new LightningDbContext(dbOpts);

        // Mocks
        _mockClock = new Mock<IClock>();
        _mockReconciler = new Mock<IReceiptReconciliationOrchestrator>();

        // Setup clock mock
        _mockClock.Setup(x => x.UtcNow).Returns(DateTimeOffset.UtcNow);

        // SUT
        _receiptItemService = new ReceiptItemsService(
            _dbContext,
            _mockClock.Object,
            _mockReconciler.Object
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task AddItemAsync_WithValidDto_ShouldAddItem()
    {
        // Arrange
        var receipt = new Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        var dto = TestHelpers.CreateValidCreateReceiptItemRequest();

        // Act
        var result = await _receiptItemService.AddItemAsync(receipt.Id, dto);

        // Assert
        result.Should().NotBeNull();
        result!.Label.Should().Be("Test Item");
        result.Qty.Should().Be(3);
        result.UnitPrice.Should().Be(7.50m);
        result.Unit.Should().Be("lb");
        result.Category.Should().Be("Produce");
        result.Notes.Should().Be("Fresh produce");
        result.Discount.Should().Be(2.00m);
        result.Tax.Should().Be(1.25m);

        // Verify database
        var savedItem = await _dbContext.ReceiptItems
            .FirstOrDefaultAsync(i => i.ReceiptId == receipt.Id);

        savedItem.Should().NotBeNull();
        savedItem!.Label.Should().Be("Test Item");
        savedItem.Qty.Should().Be(3);
        savedItem.UnitPrice.Should().Be(7.50m);
    }

    [Fact]
    public async Task AddItemAsync_WithNonExistentReceipt_ShouldReturnNull()
    {
        // Arrange
        var dto = TestHelpers.CreateValidCreateReceiptItemRequest();

        // Act
        var result = await _receiptItemService.AddItemAsync(Guid.NewGuid(), dto);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task AddItemAsync_WithNullDto_ShouldThrowArgumentNullException()
    {
        // Arrange
        var receipt = new Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        await _receiptItemService.Invoking(s => s.AddItemAsync(receipt.Id, null!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task AddItemAsync_WithZeroQuantity_ShouldThrowArgumentException()
    {
        // Arrange
        var receipt = new Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        var dto = new CreateReceiptItemRequest("Test Item", 0, 10.00m);

        // Act & Assert
        await _receiptItemService.Invoking(s => s.AddItemAsync(receipt.Id, dto))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*{TestHelpers.ValidationMessages.QtyMustBePositive}*");
    }

    [Fact]
    public async Task AddItemAsync_WithNegativeUnitPrice_ShouldThrowArgumentException()
    {
        // Arrange
        var receipt = new Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        var dto = new CreateReceiptItemRequest("Test Item", 1, -10.00m);

        // Act & Assert
        await _receiptItemService.Invoking(s => s.AddItemAsync(receipt.Id, dto))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*{TestHelpers.ValidationMessages.UnitPriceCannotBeNegative}*");
    }

    [Fact]
    public async Task AddItemAsync_WithNegativeDiscount_ShouldThrowArgumentException()
    {
        // Arrange
        var receipt = new Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        var dto = new CreateReceiptItemRequest("Test Item", 1, 10.00m, Discount: -1.00m);

        // Act & Assert
        await _receiptItemService.Invoking(s => s.AddItemAsync(receipt.Id, dto))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*{TestHelpers.ValidationMessages.DiscountCannotBeNegative}*");
    }

    [Fact]
    public async Task AddItemAsync_WithNegativeTax_ShouldThrowArgumentException()
    {
        // Arrange
        var receipt = new Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        var dto = new CreateReceiptItemRequest("Test Item", 1, 10.00m, Tax: -1.00m);

        // Act & Assert
        await _receiptItemService.Invoking(s => s.AddItemAsync(receipt.Id, dto))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage($"*{TestHelpers.ValidationMessages.TaxCannotBeNegative}*");
    }

    [Fact]
    public async Task AddItemAsync_WithAdjustmentLabel_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var receipt = new Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        var dto = new CreateReceiptItemRequest("Adjustment", 1, 10.00m);

        // Act & Assert
        await _receiptItemService.Invoking(s => s.AddItemAsync(receipt.Id, dto))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{TestHelpers.ValidationMessages.AdjustmentSystemManaged}*");
    }

    [Fact]
    public async Task AddItemAsync_WithNonItemLabel_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var receipt = new Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        var dto = new CreateReceiptItemRequest("Subtotal", 1, 10.00m);

        // Act & Assert
        await _receiptItemService.Invoking(s => s.AddItemAsync(receipt.Id, dto))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{TestHelpers.ValidationMessages.NonItemLabels}*");
    }

    [Fact]
    public async Task AddItemAsync_WithExcessiveDiscount_ShouldClampDiscount()
    {
        // Arrange
        var receipt = new Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        var dto = new CreateReceiptItemRequest("Test Item", 2, 10.00m, Discount: 25.00m); // Discount > line subtotal

        // Act
        var result = await _receiptItemService.AddItemAsync(receipt.Id, dto);

        // Assert
        result.Should().NotBeNull();
        result!.Discount.Should().Be(20.00m); // Clamped to line subtotal
    }

    [Fact]
    public async Task AddItemAsync_WithQuantityInLabel_ShouldNormalizeLabel()
    {
        // Arrange
        var receipt = new Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        var dto = new CreateReceiptItemRequest("2x Coffee", 2, 5.00m);

        // Act
        var result = await _receiptItemService.AddItemAsync(receipt.Id, dto);

        // Assert
        result.Should().NotBeNull();
        result!.Label.Should().Be("Coffee"); // Quantity prefix removed
    }

    [Fact]
    public async Task UpdateItemAsync_WithValidDto_ShouldUpdateItem()
    {
        // Arrange
        var receipt = new Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed
        };
        var item = new ReceiptItem
        {
            Id = Guid.NewGuid(),
            ReceiptId = receipt.Id,
            Label = "Original Item",
            Qty = 1,
            UnitPrice = 10.00m,
            Version = 1
        };
        _dbContext.Receipts.Add(receipt);
        _dbContext.ReceiptItems.Add(item);
        await _dbContext.SaveChangesAsync();

        var dto = TestHelpers.CreateValidUpdateReceiptItemDto(version: 1);

        // Act
        var result = await _receiptItemService.UpdateItemAsync(receipt.Id, item.Id, dto);

        // Assert
        result.Should().NotBeNull();
        result!.Label.Should().Be("Updated Item");
        result.Qty.Should().Be(3);
        result.UnitPrice.Should().Be(12.00m);
        result.Unit.Should().Be("ea");
        result.Category.Should().Be("Updated Category");
        result.Notes.Should().Be("Updated notes");
    }

    [Fact]
    public async Task UpdateItemAsync_WithNonExistentItem_ShouldReturnNull()
    {
        // Arrange
        var receipt = new Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        var dto = TestHelpers.CreateValidUpdateReceiptItemDto();

        // Act
        var result = await _receiptItemService.UpdateItemAsync(receipt.Id, Guid.NewGuid(), dto);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateItemAsync_WithNullDto_ShouldThrowArgumentNullException()
    {
        // Arrange
        var receipt = new Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed
        };
        var item = new ReceiptItem
        {
            Id = Guid.NewGuid(),
            ReceiptId = receipt.Id,
            Label = "Test Item",
            Qty = 1,
            UnitPrice = 10.00m,
            Version = 1
        };
        _dbContext.Receipts.Add(receipt);
        _dbContext.ReceiptItems.Add(item);
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        await _receiptItemService.Invoking(s => s.UpdateItemAsync(receipt.Id, item.Id, null!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UpdateItemAsync_WithSystemAdjustment_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var receipt = new Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed
        };
        var item = new ReceiptItem
        {
            Id = Guid.NewGuid(),
            ReceiptId = receipt.Id,
            Label = "Adjustment",
            Qty = 1,
            UnitPrice = 10.00m,
            IsSystemGenerated = true,
            Version = 1
        };
        _dbContext.Receipts.Add(receipt);
        _dbContext.ReceiptItems.Add(item);
        await _dbContext.SaveChangesAsync();

        // Create a DTO that tries to change the label to "Adjustment" to trigger the validation
        var dto = new UpdateReceiptItemDto
        {
            Label = "Adjustment", // Keep the same label to trigger the system-generated check
            Qty = 1,
            UnitPrice = 10.00m,
            Version = 1
        };

        // Act & Assert
        await _receiptItemService.Invoking(s => s.UpdateItemAsync(receipt.Id, item.Id, dto))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{TestHelpers.ValidationMessages.AdjustmentCannotModify}*");
    }

    [Fact]
    public async Task UpdateItemAsync_WithConcurrencyConflict_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var receipt = new Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed
        };
        var item = new ReceiptItem
        {
            Id = Guid.NewGuid(),
            ReceiptId = receipt.Id,
            Label = "Test Item",
            Qty = 1,
            UnitPrice = 10.00m,
            Version = 1
        };
        _dbContext.Receipts.Add(receipt);
        _dbContext.ReceiptItems.Add(item);
        await _dbContext.SaveChangesAsync();

        // Simulate concurrent update by changing version
        item.Version = 2;
        await _dbContext.SaveChangesAsync();

        var dto = TestHelpers.CreateValidUpdateReceiptItemDto(version: 1); // Stale version

        // Act & Assert
        await _receiptItemService.Invoking(s => s.UpdateItemAsync(receipt.Id, item.Id, dto))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{TestHelpers.ValidationMessages.ConcurrencyConflict}*");
    }

    [Fact]
    public async Task DeleteItemAsync_WithExistingItem_ShouldDeleteItem()
    {
        // Arrange
        var receipt = new Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed
        };
        var item = new ReceiptItem
        {
            Id = Guid.NewGuid(),
            ReceiptId = receipt.Id,
            Label = "Test Item",
            Qty = 1,
            UnitPrice = 10.00m,
            Version = 1
        };
        _dbContext.Receipts.Add(receipt);
        _dbContext.ReceiptItems.Add(item);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _receiptItemService.DeleteItemAsync(receipt.Id, item.Id, 1);

        // Assert
        result.Should().BeTrue();

        var deletedItem = await _dbContext.ReceiptItems.FindAsync(item.Id);
        deletedItem.Should().BeNull();
    }

    [Fact]
    public async Task DeleteItemAsync_WithNonExistentItem_ShouldReturnFalse()
    {
        // Arrange
        var receipt = new Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _receiptItemService.DeleteItemAsync(receipt.Id, Guid.NewGuid(), 1);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteItemAsync_WithSystemAdjustment_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var receipt = new Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed
        };
        var item = new ReceiptItem
        {
            Id = Guid.NewGuid(),
            ReceiptId = receipt.Id,
            Label = "Adjustment",
            Qty = 1,
            UnitPrice = 10.00m,
            IsSystemGenerated = true,
            Version = 1
        };
        _dbContext.Receipts.Add(receipt);
        _dbContext.ReceiptItems.Add(item);
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        await _receiptItemService.Invoking(s => s.DeleteItemAsync(receipt.Id, item.Id, 1))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{TestHelpers.ValidationMessages.AdjustmentCannotDelete}*");
    }

    [Fact]
    public async Task DeleteItemAsync_WithConcurrencyConflict_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var receipt = new Receipt
        {
            Id = Guid.NewGuid(),
            Status = ReceiptStatus.Parsed
        };
        var item = new ReceiptItem
        {
            Id = Guid.NewGuid(),
            ReceiptId = receipt.Id,
            Label = "Test Item",
            Qty = 1,
            UnitPrice = 10.00m,
            Version = 1
        };
        _dbContext.Receipts.Add(receipt);
        _dbContext.ReceiptItems.Add(item);
        await _dbContext.SaveChangesAsync();

        // Simulate concurrent update by changing version
        item.Version = 2;
        await _dbContext.SaveChangesAsync();

        // Act & Assert
        await _receiptItemService.Invoking(s => s.DeleteItemAsync(receipt.Id, item.Id, 1)) // Stale version
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Concurrency conflict while deleting the item.*");
    }
}

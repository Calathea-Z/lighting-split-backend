using Api.Abstractions.Receipts;
using Api.Dtos.Receipts.Requests;
using Api.Dtos.Receipts.Requests.Items;
using Api.Models;

namespace Tests;

public static class TestHelpers
{
    public static CreateReceiptDto CreateValidReceiptDto()
    {
        return new CreateReceiptDto
        {
            Items = new List<CreateReceiptItemDto>
            {
                new()
                {
                    Label = "Test Item 1",
                    Qty = 2,
                    UnitPrice = 10.50m,
                    Unit = "ea",
                    Category = "Food",
                    Notes = "Test notes"
                },
                new()
                {
                    Label = "Test Item 2",
                    Qty = 1,
                    UnitPrice = 5.25m,
                    Discount = 1.00m,
                    Tax = 0.50m
                }
            }
        };
    }

    public static CreateReceiptItemDto CreateValidReceiptItemDto()
    {
        return new CreateReceiptItemDto
        {
            Label = "Test Item",
            Qty = 3,
            UnitPrice = 7.50m,
            Unit = "lb",
            Category = "Produce",
            Notes = "Fresh produce",
            Discount = 2.00m,
            Tax = 1.25m
        };
    }

    public static Receipt CreateTestReceipt(Guid? id = null, ReceiptStatus? status = null)
    {
        return new Receipt
        {
            Id = id ?? Guid.NewGuid(),
            Status = status ?? ReceiptStatus.Parsed,
            OwnerUserId = TestConstants.TestUser,
            SubTotal = 25.00m,
            Tax = 2.50m,
            Tip = 5.00m,
            Total = 32.50m,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
            Items = new List<ReceiptItem>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Label = TestConstants.TestItemLabel,
                    Qty = 2,
                    UnitPrice = 10.00m,
                    LineSubtotal = 20.00m,
                    Tax = 2.00m,
                    LineTotal = 22.00m
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    Label = TestConstants.TestItemLabel2,
                    Qty = 1,
                    UnitPrice = 5.00m,
                    LineSubtotal = 5.00m,
                    Tax = 0.50m,
                    LineTotal = 5.50m
                }
            }
        };
    }

    public static ReceiptItem CreateTestReceiptItem(Guid receiptId, Guid? id = null)
    {
        return new ReceiptItem
        {
            Id = id ?? Guid.NewGuid(),
            ReceiptId = receiptId,
            Label = TestConstants.TestItemLabel,
            Qty = 2,
            UnitPrice = 10.00m,
            Unit = "ea",
            Category = TestConstants.TestCategory,
            Notes = TestConstants.TestNotes,
            Position = 0,
            LineSubtotal = 20.00m,
            Tax = 2.00m,
            LineTotal = 22.00m,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public static UpdateTotalsDto CreateValidUpdateTotalsDto()
    {
        return new UpdateTotalsDto(30.00m, 3.00m, 6.00m, 39.00m);
    }

    public static UpdateReceiptItemDto CreateValidUpdateReceiptItemDto(uint version = 1)
    {
        return new UpdateReceiptItemDto
        {
            Label = "Updated Item",
            Qty = 3,
            UnitPrice = 12.00m,
            Unit = "ea",
            Category = "Updated Category",
            Notes = "Updated notes",
            Discount = 3.00m,
            Tax = 2.50m,
            Version = version
        };
    }

    public static class TestData
    {
        public static readonly List<string> ValidStatuses = new()
        {
            "PendingParse",
            "Parsed",
            "FailedParse"
        };

        public static readonly List<string> ValidUnits = new()
        {
            "ea", "lb", "oz", "kg", "g", "l", "ml", "pkg", "box"
        };

        public static readonly List<string> ValidCategories = new()
        {
            "Food", "Beverages", "Household", "Electronics", "Clothing", "Produce", "Dairy", "Meat"
        };
    }

    public static class ValidationMessages
    {
        public const string BodyRequired = "Body is required.";
        public const string ItemsRequired = "At least one item is required.";
        public const string InvalidQuantities = "Item quantities must be > 0 and prices must be >= 0.";
        public const string FileRequired = "File is required.";
        public const string FileTooLarge = "File too large (>20MB).";
        public const string ConcurrencyConflict = "Concurrency conflict. Reload the item and try again.";
    }

    public static class TestConstants
    {
        public const string ReceiptsContainer = "receipts";
        public const string TestUserId1 = "user1";
        public const string TestUserId2 = "user2";
        public const string TestUser = "test-user";
        public const string TestStoreName = "Test Store";
        public const string TestItemLabel = "Test Item";
        public const string TestItemLabel2 = "Another Item";
        public const string TestCategory = "Test Category";
        public const string TestNotes = "Test notes";
        public const string TestFileName = "test.jpg";
        public const string TestContentType = "image/jpeg";
    }
}

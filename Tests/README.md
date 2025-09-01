# Receipt Service Tests

This directory contains comprehensive unit tests for the `ReceiptService` class and related functionality.

## Test Structure

### Test Files

- **`ReceiptServiceTests.cs`** - Main test class covering all `ReceiptService` methods
- **`ReceiptServiceUploadTests.cs`** - Specialized tests for file upload functionality
- **`TestHelpers.cs`** - Helper methods and test data factories
- **`TestConfiguration.cs`** - Test configuration and setup utilities

### Test Coverage

The tests cover the following areas:

#### ReceiptService Core Methods
- ✅ `CreateAsync` - Receipt creation with validation
- ✅ `GetByIdAsync` - Receipt retrieval
- ✅ `ListAsync` - Receipt listing with filtering and pagination
- ✅ `DeleteAsync` - Receipt deletion with blob cleanup
- ✅ `UpdateTotalsAsync` - Receipt totals updates
- ✅ `MarkParseFailedAsync` - Parse failure handling

#### Receipt Item Management
- ✅ `AddItemAsync` - Adding items to receipts
- ✅ `UpdateItemAsync` - Updating items with concurrency control
- ✅ `DeleteItemAsync` - Deleting items from receipts

#### File Upload Functionality
- ✅ `UploadAsync` - File upload with blob storage
- ✅ File validation (size, type, etc.)
- ✅ Blob metadata handling
- ✅ Parse queue integration

### Test Categories

1. **Happy Path Tests** - Normal operation scenarios
2. **Validation Tests** - Input validation and error handling
3. **Edge Case Tests** - Boundary conditions and unusual scenarios
4. **Integration Tests** - Database and external service interactions
5. **Concurrency Tests** - Optimistic concurrency control

## Running Tests

### Prerequisites

Make sure you have the following packages installed:
- `Microsoft.NET.Test.Sdk`
- `xunit`
- `Moq`
- `Microsoft.EntityFrameworkCore.InMemory`
- `FluentAssertions`

### Running Tests

From the `backend` directory:

```powershell
# Run all tests
dotnet test

# Run tests with verbose output
dotnet test --verbosity normal

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run specific test class
dotnet test --filter "FullyQualifiedName~ReceiptServiceTests"

# Run specific test method
dotnet test --filter "FullyQualifiedName~CreateAsync_WithValidDto_ShouldCreateReceipt"
```

## Coverage Goals

The test suite aims for:

- **Line Coverage**: >90%
- **Branch Coverage**: >85%
- **Method Coverage**: 100%

Run coverage analysis to identify gaps:

```powershell
dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage
```

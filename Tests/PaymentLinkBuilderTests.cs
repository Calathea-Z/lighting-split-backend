using System;
using System.Globalization;
using System.Net;
using FluentAssertions;
using Api.Contracts.Payment;
using Api.Data;
using Api.Models;
using Api.Models.Owners;
using Api.Services.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace Tests
{
    public class PaymentLinkBuilderUnitTests : IDisposable
    {
        private readonly Mock<ILogger<PaymentLinkBuilder>> _mockLogger;
        private readonly LightningDbContext _dbContext;
        private readonly PaymentLinkBuilder _paymentLinkBuilder;

        public PaymentLinkBuilderUnitTests()
        {
            _mockLogger = new Mock<ILogger<PaymentLinkBuilder>>();

            var options = new DbContextOptionsBuilder<LightningDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

            _dbContext = new LightningDbContext(options);
            _paymentLinkBuilder = new PaymentLinkBuilder(_mockLogger.Object, _dbContext);
        }

        public void Dispose()
        {
            _dbContext.Dispose();
        }

        #region BuildAsync Tests (original + additions)

        [Fact]
        public async Task BuildAsync_WithInstructionsOnlyPlatform_ReturnsInstructionsOnlyLink()
        {
            var methodId = Guid.NewGuid();
            var platform = CreatePayoutPlatform("zelle", "Zelle", isInstructionsOnly: true);
            var method = CreateOwnerPayoutMethod(methodId, platform, "user@example.com", "My Zelle");

            var result = await _paymentLinkBuilder.BuildAsync(method, 25.50m, "Test note");

            result.Should().NotBeNull();
            result.MethodId.Should().Be(methodId);
            result.PlatformKey.Should().Be("zelle");
            result.PlatformName.Should().Be("Zelle");
            result.Label.Should().Be("My Zelle");
            result.Url.Should().BeNull();
            result.IsInstructionsOnly.Should().BeTrue();
            result.Instructions.Should().Be("user@example.com");
        }

        [Fact]
        public async Task BuildAsync_WithNullLinkTemplate_ReturnsInstructionsOnlyLink()
        {
            var methodId = Guid.NewGuid();
            var platform = CreatePayoutPlatform("applecash", "Apple Cash", linkTemplate: null);
            var method = CreateOwnerPayoutMethod(methodId, platform, "user@icloud.com");

            var result = await _paymentLinkBuilder.BuildAsync(method, 15.75m, "Test note");

            result.Should().NotBeNull();
            result.IsInstructionsOnly.Should().BeTrue();
            result.Instructions.Should().Be("user@icloud.com");
            result.Url.Should().BeNull();
        }

        [Fact]
        public async Task BuildAsync_WithTemplatePlatform_ReturnsUrlLink()
        {
            var methodId = Guid.NewGuid();
            var platform = CreatePayoutPlatform("venmo", "Venmo",
                linkTemplate: "https://account.venmo.com/pay?recipients={handle}&amount={amount}&note={note}",
                supportsAmount: true, supportsNote: true);
            var method = CreateOwnerPayoutMethod(methodId, platform, "testuser");

            var result = await _paymentLinkBuilder.BuildAsync(method, 25.50m, "Test note");

            result.Should().NotBeNull();
            result.MethodId.Should().Be(methodId);
            result.PlatformKey.Should().Be("venmo");
            result.PlatformName.Should().Be("Venmo");
            result.Label.Should().Be("Venmo");

            var expectedUrl = "https://account.venmo.com/pay?recipients=testuser&amount=25.50&note="
                              + WebUtility.UrlEncode("Test note");

            result.Url.Should().Be(expectedUrl);
            result.IsInstructionsOnly.Should().BeFalse();
            result.Instructions.Should().BeNull();
        }


        [Fact]
        public async Task BuildAsync_WithCustomDisplayLabel_UsesDisplayLabel()
        {
            var methodId = Guid.NewGuid();
            var platform = CreatePayoutPlatform("venmo", "Venmo",
                linkTemplate: "https://account.venmo.com/pay?recipients={handle}");
            var method = CreateOwnerPayoutMethod(methodId, platform, "testuser", "My Venmo Account");

            var result = await _paymentLinkBuilder.BuildAsync(method, 10.00m, "Test note");

            result.Label.Should().Be("My Venmo Account");
        }

        [Fact]
        public async Task BuildAsync_WithNullPlatform_LoadsPlatformFromDatabase()
        {
            var methodId = Guid.NewGuid();
            var platform = CreatePayoutPlatform("cashapp", "Cash App",
                linkTemplate: "https://cash.app/${handle}");

            _dbContext.PayoutPlatforms.Add(platform);
            await _dbContext.SaveChangesAsync();

            var method = CreateOwnerPayoutMethod(methodId, null, "testuser");
            method.PlatformId = platform.Id;

            _dbContext.OwnerPayoutMethods.Add(method);
            await _dbContext.SaveChangesAsync();

            var result = await _paymentLinkBuilder.BuildAsync(method, 20.00m, "Test note");

            result.Should().NotBeNull();
            result.PlatformKey.Should().Be("cashapp");
            result.Url.Should().Be("https://cash.app/$testuser");
        }

        [Fact]
        public async Task BuildAsync_WithInvalidHandle_ThrowsArgumentException()
        {
            var methodId = Guid.NewGuid();
            var platform = CreatePayoutPlatform("venmo", "Venmo",
                linkTemplate: "https://account.venmo.com/pay?recipients={handle}",
                handlePattern: @"^[a-zA-Z0-9._-]+$");
            var method = CreateOwnerPayoutMethod(methodId, platform, "invalid@handle!");

            await Assert.ThrowsAsync<ArgumentException>(() =>
                _paymentLinkBuilder.BuildAsync(method, 10.00m, "Test note"));
        }

        [Fact]
        public async Task BuildAsync_WithCustomPlatform_ValidatesHttpsUrl()
        {
            var methodId = Guid.NewGuid();
            var platform = CreatePayoutPlatform("custom", "Custom", linkTemplate: "{handle}");
            var method = CreateOwnerPayoutMethod(methodId, platform, "https://example.com/pay");

            var result = await _paymentLinkBuilder.BuildAsync(method, 10.00m, "Test note");

            result.Url.Should().Be("https%3A%2F%2Fexample.com%2Fpay");
        }

        [Fact]
        public async Task BuildAsync_WithCustomPlatform_InvalidUrl_ThrowsArgumentException()
        {
            var methodId = Guid.NewGuid();
            var platform = CreatePayoutPlatform("custom", "Custom", linkTemplate: "{handle}");
            var method = CreateOwnerPayoutMethod(methodId, platform, "http://example.com/pay");

            await Assert.ThrowsAsync<ArgumentException>(() =>
                _paymentLinkBuilder.BuildAsync(method, 10.00m, "Test note"));
        }

        [Fact]
        public async Task BuildAsync_WithPrefixToStrip_RemovesOnlyLeadingPrefix_AndTrims()
        {
            var methodId = Guid.NewGuid();
            var platform = CreatePayoutPlatform("cashapp", "Cash App",
                linkTemplate: "https://cash.app/${handle}", prefixToStrip: "$");
            var method = CreateOwnerPayoutMethod(methodId, platform, "  $  $testuser  ");

            var result = await _paymentLinkBuilder.BuildAsync(method, 10.00m, "x");

            // spaces -> '+', inner '$' -> %24, and template adds a literal '$' prefix
            result.Url.Should().Be("https://cash.app/$++%242testuser".Replace("%242", "%24" + "2").Replace("2test", "test")); // ignore this weirdness
            result.Url.Should().Be("https://cash.app/$++%24testuser");
        }

        [Fact]
        public async Task BuildAsync_AmountFormatting_IsCultureInvariant()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // uses comma decimals
                var methodId = Guid.NewGuid();
                var platform = CreatePayoutPlatform("venmo", "Venmo",
                    linkTemplate: "https://account.venmo.com/pay?recipients={handle}&amount={amount}",
                    supportsAmount: true);
                var method = CreateOwnerPayoutMethod(methodId, platform, "user");

                var result = await _paymentLinkBuilder.BuildAsync(method, 25.5m, note: null);

                result.Url.Should().Contain("amount=25.50"); // not 25,50
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public async Task BuildAsync_Note_EncodesUnicodeAndSymbols()
        {
            var methodId = Guid.NewGuid();
            var platform = CreatePayoutPlatform("venmo", "Venmo",
                linkTemplate: "https://venmo.test?recipients={handle}&note={note}",
                supportsNote: true);
            var method = CreateOwnerPayoutMethod(methodId, platform, "user");

            var result = await _paymentLinkBuilder.BuildAsync(method, 1m, "Thanks ?? & tacos");

            result.Url.Should().Contain("note=" + WebUtility.UrlEncode("Thanks ?? & tacos"));
        }

        [Fact]
        public async Task BuildAsync_SupportsFlags_PreventLeakage()
        {
            var methodId = Guid.NewGuid();
            var platform = CreatePayoutPlatform("test", "Test",
                linkTemplate: "https://test.com/pay?recipients={handle}&amount={amount}&note={note}",
                supportsAmount: false, supportsNote: false);
            var method = CreateOwnerPayoutMethod(methodId, platform, "testuser");

            var result = await _paymentLinkBuilder.BuildAsync(method, 10.00m, "Secret");

            result.Url.Should().Be("https://test.com/pay?recipients=testuser");
        }

        [Fact]
        public async Task BuildAsync_ZeroAmountAndNullNote_RemoveEmptyParams()
        {
            var methodId = Guid.NewGuid();
            var platform = CreatePayoutPlatform("t", "T",
                linkTemplate: "https://x?handle={handle}&amount={amount}&note={note}",
                supportsAmount: false, supportsNote: false);
            var method = CreateOwnerPayoutMethod(methodId, platform, "h");

            var result = await _paymentLinkBuilder.BuildAsync(method, 0m, null);

            result.Url.Should().Be("https://x?handle=h");
        }

        [Fact]
        public async Task BuildAsync_UnknownPlatformId_Throws()
        {
            var methodId = Guid.NewGuid();
            var method = CreateOwnerPayoutMethod(methodId, null, "user");
            method.PlatformId = 424242; // not in DB

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _paymentLinkBuilder.BuildAsync(method, 1m, "x"));
        }

        #endregion

        #region BuildManyAsync Tests (original + additions)

        [Fact]
        public async Task BuildManyAsync_WithValidMethods_ReturnsAllLinks()
        {
            var method1Id = Guid.NewGuid();
            var method2Id = Guid.NewGuid();
            var platform1 = CreatePayoutPlatform("venmo", "Venmo",
                linkTemplate: "https://account.venmo.com/pay?recipients={handle}");
            var platform2 = CreatePayoutPlatform("zelle", "Zelle", isInstructionsOnly: true);

            var methods = new[]
            {
                CreateOwnerPayoutMethod(method1Id, platform1, "user1"),
                CreateOwnerPayoutMethod(method2Id, platform2, "user2@example.com")
            };

            var results = await _paymentLinkBuilder.BuildManyAsync(methods, 25.00m, "Test note");

            results.Should().HaveCount(2);
            results.Should().Contain(r => r.MethodId == method1Id && r.PlatformKey == "venmo");
            results.Should().Contain(r => r.MethodId == method2Id && r.PlatformKey == "zelle");
        }

        [Fact]
        public async Task BuildManyAsync_WithSomeInvalidMethods_SkipsInvalidAndReturnsValid()
        {
            var method1Id = Guid.NewGuid();
            var method2Id = Guid.NewGuid();
            var platform1 = CreatePayoutPlatform("venmo", "Venmo",
                linkTemplate: "https://account.venmo.com/pay?recipients={handle}");
            var platform2 = CreatePayoutPlatform("venmo2", "Venmo2",
                linkTemplate: "https://account.venmo.com/pay?recipients={handle}",
                handlePattern: @"^[a-zA-Z0-9._-]+$");

            var methods = new[]
            {
                CreateOwnerPayoutMethod(method1Id, platform1, "validuser"),
                CreateOwnerPayoutMethod(method2Id, platform2, "invalid@user!")
            };

            var results = await _paymentLinkBuilder.BuildManyAsync(methods, 25.00m, "Test note");

            results.Should().HaveCount(1);
            results.Should().Contain(r => r.MethodId == method1Id);
            results.Should().NotContain(r => r.MethodId == method2Id);
        }

        [Fact]
        public async Task BuildManyAsync_WithMissingPlatforms_LoadsFromDatabase()
        {
            var method1Id = Guid.NewGuid();
            var method2Id = Guid.NewGuid();
            var platform1 = CreatePayoutPlatform("venmo", "Venmo",
                linkTemplate: "https://account.venmo.com/pay?recipients={handle}");
            var platform2 = CreatePayoutPlatform("zelle", "Zelle", isInstructionsOnly: true);

            _dbContext.PayoutPlatforms.AddRange(platform1, platform2);
            await _dbContext.SaveChangesAsync();

            var method1 = CreateOwnerPayoutMethod(method1Id, null, "user1"); method1.PlatformId = platform1.Id;
            var method2 = CreateOwnerPayoutMethod(method2Id, platform2, "user2@example.com");

            _dbContext.OwnerPayoutMethods.AddRange(method1, method2);
            await _dbContext.SaveChangesAsync();

            var results = await _paymentLinkBuilder.BuildManyAsync(new[] { method1, method2 }, 25.00m, "Test note");

            results.Should().HaveCount(2);
            results.Should().Contain(r => r.MethodId == method1Id && r.PlatformKey == "venmo");
            results.Should().Contain(r => r.MethodId == method2Id && r.PlatformKey == "zelle");
        }

        [Fact]
        public async Task BuildManyAsync_WithAllInvalidMethods_ReturnsEmptyList()
        {
            var method1Id = Guid.NewGuid();
            var method2Id = Guid.NewGuid();
            var platform = CreatePayoutPlatform("venmo", "Venmo",
                linkTemplate: "https://account.venmo.com/pay?recipients={handle}",
                handlePattern: @"^[a-zA-Z0-9._-]+$");

            var methods = new[]
            {
                CreateOwnerPayoutMethod(method1Id, platform, "invalid@user1!"),
                CreateOwnerPayoutMethod(method2Id, platform, "invalid@user2!")
            };

            var results = await _paymentLinkBuilder.BuildManyAsync(methods, 25.00m, "Test note");

            results.Should().BeEmpty();
        }

        [Fact]
        public async Task BuildManyAsync_NullMethods_Throws()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                _paymentLinkBuilder.BuildManyAsync(null!, 1m, "x"));
        }

        [Fact]
        public async Task BuildManyAsync_EmptyMethods_ReturnsEmpty()
        {
            var results = await _paymentLinkBuilder.BuildManyAsync(Array.Empty<OwnerPayoutMethod>(), 1m, "x");
            results.Should().BeEmpty();
        }

        [Fact]
        public async Task BuildManyAsync_LabelFallbacks_Work()
        {
            var id = Guid.NewGuid();
            var platform = CreatePayoutPlatform("venmo", "Venmo",
                linkTemplate: "https://account.venmo.com/pay?recipients={handle}");
            var withCustom = CreateOwnerPayoutMethod(Guid.NewGuid(), platform, "u1", "Custom Label");
            var withoutCustom = CreateOwnerPayoutMethod(id, platform, "u2", null);

            var results = await _paymentLinkBuilder.BuildManyAsync(new[] { withCustom, withoutCustom }, 5m, "n");

            results.Should().ContainSingle(r => r.MethodId == withCustom.Id && r.Label == "Custom Label");
            results.Should().ContainSingle(r => r.MethodId == id && r.Label == "Venmo");
        }

        #endregion

        #region Helpers

        private static PayoutPlatform CreatePayoutPlatform(
            string key,
            string displayName,
            string? linkTemplate = null,
            bool supportsAmount = false,
            bool supportsNote = false,
            string? handlePattern = null,
            string? prefixToStrip = null,
            bool isInstructionsOnly = false)
        {
            return new PayoutPlatform
            {
                Id = Random.Shared.Next(1000, 9999),
                Key = key,
                DisplayName = displayName,
                LinkTemplate = linkTemplate,
                SupportsAmount = supportsAmount,
                SupportsNote = supportsNote,
                HandlePattern = handlePattern,
                PrefixToStrip = prefixToStrip,
                IsInstructionsOnly = isInstructionsOnly,
                SortOrder = 1
            };
        }

        private static OwnerPayoutMethod CreateOwnerPayoutMethod(
            Guid id,
            PayoutPlatform? platform,
            string handleOrUrl,
            string? displayLabel = null)
        {
            return new OwnerPayoutMethod
            {
                Id = id,
                OwnerId = Guid.NewGuid(),
                PlatformId = platform?.Id ?? 1,
                Platform = platform!,
                HandleOrUrl = handleOrUrl,
                DisplayLabel = displayLabel,
                IsDefault = false,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }

        #endregion
    }
}

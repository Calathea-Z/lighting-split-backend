using System.Globalization;
using System.Net;
using Api.Data;
using Api.Models;
using Api.Models.Owners;
using Api.Services.Payments;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Tests.UnitTests
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

        public void Dispose() => _dbContext.Dispose();

        /* ----------------------- BuildAsync ----------------------- */

        [Fact]
        public async Task BuildAsync_InstructionsOnlyPlatform_ReturnsInstructionsOnly()
        {
            var methodId = Guid.NewGuid();
            var platform = CreatePayoutPlatform("zelle", "Zelle", isInstructionsOnly: true);
            var method = CreateOwnerPayoutMethod(methodId, platform, "user@example.com", "My Zelle");

            var r = await _paymentLinkBuilder.BuildAsync(method, 25.50m, "Test note");

            r.MethodId.Should().Be(methodId);
            r.PlatformKey.Should().Be("zelle");
            r.Label.Should().Be("My Zelle");
            r.IsInstructionsOnly.Should().BeTrue();
            r.Instructions.Should().Be("user@example.com");
            r.Url.Should().BeNull();
        }

        [Fact]
        public async Task BuildAsync_NullTemplate_TreatedAsInstructionsOnly()
        {
            var platform = CreatePayoutPlatform("applecash", "Apple Cash", linkTemplate: null);
            var method = CreateOwnerPayoutMethod(Guid.NewGuid(), platform, "user@icloud.com");

            var r = await _paymentLinkBuilder.BuildAsync(method, 15.75m, "Test note");

            r.IsInstructionsOnly.Should().BeTrue();
            r.Instructions.Should().Be("user@icloud.com");
            r.Url.Should().BeNull();
        }

        [Fact]
        public async Task BuildAsync_TemplatePlatform_BuildsEncodedUrl()
        {
            var platform = CreatePayoutPlatform(
                "venmo", "Venmo",
                linkTemplate: "https://account.venmo.com/pay?recipients={handle}&amount={amount}&note={note}",
                supportsAmount: true, supportsNote: true);
            var method = CreateOwnerPayoutMethod(Guid.NewGuid(), platform, "testuser");

            var r = await _paymentLinkBuilder.BuildAsync(method, 25.50m, "Test note");

            r.PlatformKey.Should().Be("venmo");
            var expected = "https://account.venmo.com/pay?recipients=testuser&amount=25.50&note=" +
                           WebUtility.UrlEncode("Test note");
            r.Url.Should().Be(expected);
        }

        [Fact]
        public async Task BuildAsync_CustomDisplayLabel_Wins()
        {
            var platform = CreatePayoutPlatform("venmo", "Venmo",
                linkTemplate: "https://account.venmo.com/pay?recipients={handle}");
            var method = CreateOwnerPayoutMethod(Guid.NewGuid(), platform, "testuser", "My Venmo Account");

            var r = await _paymentLinkBuilder.BuildAsync(method, 10.00m, "n");

            r.Label.Should().Be("My Venmo Account");
        }

        [Fact]
        public async Task BuildAsync_NullPlatform_LoadsFromDb()
        {
            var platform = CreatePayoutPlatform("cashapp", "Cash App", linkTemplate: "https://cash.app/${handle}");
            _dbContext.PayoutPlatforms.Add(platform);
            await _dbContext.SaveChangesAsync();

            var method = CreateOwnerPayoutMethod(Guid.NewGuid(), null, "testuser");
            method.PlatformId = platform.Id;
            _dbContext.OwnerPayoutMethods.Add(method);
            await _dbContext.SaveChangesAsync();

            var r = await _paymentLinkBuilder.BuildAsync(method, 20.00m, "n");

            r.PlatformKey.Should().Be("cashapp");
            r.Url.Should().Be("https://cash.app/$testuser");
        }

        [Fact]
        public async Task BuildAsync_InvalidHandle_Throws()
        {
            var platform = CreatePayoutPlatform(
                "venmo", "Venmo",
                linkTemplate: "https://account.venmo.com/pay?recipients={handle}",
                handlePattern: @"^[a-zA-Z0-9._-]+$");
            var method = CreateOwnerPayoutMethod(Guid.NewGuid(), platform, "invalid@handle!");

            await Assert.ThrowsAsync<ArgumentException>(() =>
                _paymentLinkBuilder.BuildAsync(method, 10.00m, "n"));
        }

        [Fact]
        public async Task BuildAsync_CustomPlatform_MustBeHttps_NoEncodingOfEntireUrl()
        {
            var platform = CreatePayoutPlatform("custom", "Custom", linkTemplate: "{handle}");
            var method = CreateOwnerPayoutMethod(Guid.NewGuid(), platform, "https://example.com/pay");

            var r = await _paymentLinkBuilder.BuildAsync(method, 10.00m, "n");

            // Expect the URL itself, not percent-encoded
            r.Url.Should().Be("https://example.com/pay");
        }

        [Fact]
        public async Task BuildAsync_CustomPlatform_HttpUrlRejected()
        {
            var platform = CreatePayoutPlatform("custom", "Custom", linkTemplate: "{handle}");
            var method = CreateOwnerPayoutMethod(Guid.NewGuid(), platform, "http://example.com/pay");

            await Assert.ThrowsAsync<ArgumentException>(() =>
                _paymentLinkBuilder.BuildAsync(method, 10.00m, "n"));
        }

        [Fact]
        public async Task BuildAsync_PrefixToStrip_StripsLeadingOnly_AndTrims()
        {
            var platform = CreatePayoutPlatform(
                "cashapp", "Cash App",
                linkTemplate: "https://cash.app/${handle}",
                prefixToStrip: "$");
            var method = CreateOwnerPayoutMethod(Guid.NewGuid(), platform, "  $  $testuser  ");

            var r = await _paymentLinkBuilder.BuildAsync(method, 10.00m, "n");

            // Expected: leading '$' removed once, whitespace trimmed, template adds a single '$'
            r.Url.Should().Be("https://cash.app/$testuser");
        }

        [Fact]
        public async Task BuildAsync_AmountFormatting_IsCultureInvariant()
        {
            var prev = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // comma decimals normally
                var platform = CreatePayoutPlatform(
                    "venmo", "Venmo",
                    linkTemplate: "https://venmo.example?u={handle}&amount={amount}",
                    supportsAmount: true);
                var method = CreateOwnerPayoutMethod(Guid.NewGuid(), platform, "user");

                var r = await _paymentLinkBuilder.BuildAsync(method, 25.5m, note: null);

                r.Url.Should().Contain("amount=25.50"); // not 25,50
            }
            finally { CultureInfo.CurrentCulture = prev; }
        }

        [Fact]
        public async Task BuildAsync_Note_EncodesUnicode_AndSymbols()
        {
            var platform = CreatePayoutPlatform(
                "venmo", "Venmo",
                linkTemplate: "https://venmo.test?recipients={handle}&note={note}",
                supportsNote: true);
            var method = CreateOwnerPayoutMethod(Guid.NewGuid(), platform, "user");

            var r = await _paymentLinkBuilder.BuildAsync(method, 1m, "Thanks ?? & tacos");

            r.Url.Should().Contain("note=" + WebUtility.UrlEncode("Thanks ?? & tacos"));
        }

        [Fact]
        public async Task BuildAsync_SupportFlags_RemoveUnusedParams()
        {
            var platform = CreatePayoutPlatform(
                "test", "Test",
                linkTemplate: "https://test.com/pay?recipients={handle}&amount={amount}&note={note}",
                supportsAmount: false, supportsNote: false);
            var method = CreateOwnerPayoutMethod(Guid.NewGuid(), platform, "testuser");

            var r = await _paymentLinkBuilder.BuildAsync(method, 10.00m, "Secret");

            r.Url.Should().Be("https://test.com/pay?recipients=testuser");
        }

        [Fact]
        public async Task BuildAsync_ZeroAmount_And_NullNote_RemoveEmptyParams()
        {
            var platform = CreatePayoutPlatform(
                "t", "T",
                linkTemplate: "https://x?handle={handle}&amount={amount}&note={note}",
                supportsAmount: false, supportsNote: false);
            var method = CreateOwnerPayoutMethod(Guid.NewGuid(), platform, "h");

            var r = await _paymentLinkBuilder.BuildAsync(method, 0m, null);

            r.Url.Should().Be("https://x?handle=h");
        }

        [Fact]
        public async Task BuildAsync_UnknownPlatformId_Throws()
        {
            var method = CreateOwnerPayoutMethod(Guid.NewGuid(), null, "user");
            method.PlatformId = 424242; // not in DB

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _paymentLinkBuilder.BuildAsync(method, 1m, "n"));
        }

        /* ----------------------- BuildManyAsync ----------------------- */

        [Fact]
        public async Task BuildManyAsync_Valid_ReturnsAll()
        {
            var p1 = CreatePayoutPlatform("venmo", "Venmo",
                linkTemplate: "https://account.venmo.com/pay?recipients={handle}");
            var p2 = CreatePayoutPlatform("zelle", "Zelle", isInstructionsOnly: true);

            var methods = new[]
            {
                CreateOwnerPayoutMethod(Guid.NewGuid(), p1, "user1"),
                CreateOwnerPayoutMethod(Guid.NewGuid(), p2, "user2@example.com")
            };

            var results = await _paymentLinkBuilder.BuildManyAsync(methods, 25.00m, "n");

            results.Should().HaveCount(2);
            results.Should().Contain(x => x.PlatformKey == "venmo");
            results.Should().Contain(x => x.PlatformKey == "zelle");
        }

        [Fact]
        public async Task BuildManyAsync_SkipsInvalid_KeepsValid()
        {
            var p1 = CreatePayoutPlatform("venmo", "Venmo",
                linkTemplate: "https://account.venmo.com/pay?recipients={handle}");
            var p2 = CreatePayoutPlatform("venmo2", "Venmo2",
                linkTemplate: "https://account.venmo.com/pay?recipients={handle}",
                handlePattern: @"^[a-zA-Z0-9._-]+$");

            var methods = new[]
            {
                CreateOwnerPayoutMethod(Guid.NewGuid(), p1, "validuser"),
                CreateOwnerPayoutMethod(Guid.NewGuid(), p2, "invalid@user!")
            };

            var results = await _paymentLinkBuilder.BuildManyAsync(methods, 25.00m, "n");

            results.Should().HaveCount(1);
            results.Should().OnlyContain(r => r.PlatformKey == "venmo");
        }

        [Fact]
        public async Task BuildManyAsync_MissingPlatforms_LoadFromDb()
        {
            var p1 = CreatePayoutPlatform("venmo", "Venmo",
                linkTemplate: "https://account.venmo.com/pay?recipients={handle}");
            var p2 = CreatePayoutPlatform("zelle", "Zelle", isInstructionsOnly: true);

            _dbContext.PayoutPlatforms.AddRange(p1, p2);
            await _dbContext.SaveChangesAsync();

            var m1 = CreateOwnerPayoutMethod(Guid.NewGuid(), null, "user1"); m1.PlatformId = p1.Id;
            var m2 = CreateOwnerPayoutMethod(Guid.NewGuid(), p2, "user2@example.com");
            _dbContext.OwnerPayoutMethods.AddRange(m1, m2);
            await _dbContext.SaveChangesAsync();

            var results = await _paymentLinkBuilder.BuildManyAsync(new[] { m1, m2 }, 25.00m, "n");

            results.Should().HaveCount(2);
            results.Should().Contain(r => r.PlatformKey == "venmo");
            results.Should().Contain(r => r.PlatformKey == "zelle");
        }

        [Fact]
        public async Task BuildManyAsync_AllInvalid_ReturnsEmpty()
        {
            var p = CreatePayoutPlatform("venmo", "Venmo",
                linkTemplate: "https://account.venmo.com/pay?recipients={handle}",
                handlePattern: @"^[a-zA-Z0-9._-]+$");

            var methods = new[]
            {
                CreateOwnerPayoutMethod(Guid.NewGuid(), p, "invalid@user1!"),
                CreateOwnerPayoutMethod(Guid.NewGuid(), p, "invalid@user2!")
            };

            var results = await _paymentLinkBuilder.BuildManyAsync(methods, 25.00m, "n");

            results.Should().BeEmpty();
        }

        [Fact]
        public async Task BuildManyAsync_LabelFallbacks()
        {
            var p = CreatePayoutPlatform("venmo", "Venmo",
                linkTemplate: "https://account.venmo.com/pay?recipients={handle}");
            var withCustom = CreateOwnerPayoutMethod(Guid.NewGuid(), p, "u1", "Custom Label");
            var withoutCustomId = Guid.NewGuid();
            var withoutCustom = CreateOwnerPayoutMethod(withoutCustomId, p, "u2", null);

            var results = await _paymentLinkBuilder.BuildManyAsync(new[] { withCustom, withoutCustom }, 5m, "n");

            results.Should().ContainSingle(r => r.MethodId == withCustom.Id && r.Label == "Custom Label");
            results.Should().ContainSingle(r => r.MethodId == withoutCustomId && r.Label == "Venmo");
        }

        /* ----------------------- Helpers ----------------------- */

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
    }
}

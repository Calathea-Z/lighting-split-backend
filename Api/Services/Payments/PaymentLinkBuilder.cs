using Api.Contracts.Payment;
using Api.Data;
using Api.Models;
using Api.Models.Owners;
using Api.Services.Payments.Abstractions;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Api.Services.Payments;

public sealed class PaymentLinkBuilder : IPaymentLinkBuilder
{
    private readonly LightningDbContext _db;
    public PaymentLinkBuilder(LightningDbContext db) => _db = db;

    public async Task<PaymentLink> BuildAsync(OwnerPayoutMethod method, decimal amount, string note)
    {
        // Ensure Platform is loaded
        if (method.Platform is null)
        {
            method = await _db.OwnerPayoutMethods
                .Include(m => m.Platform)
                .FirstAsync(m => m.Id == method.Id);
        }

        var platform = method.Platform;

        // Instructions-only (e.g., Zelle/Apple Cash): no URL
        if (platform.LinkTemplate is null || platform.IsInstructionsOnly)
        {
            return new PaymentLink(
                method.Id,
                platform.Key,
                platform.DisplayName,
                method.DisplayLabel ?? platform.DisplayName,
                null,
                true,
                method.HandleOrUrl
            );
        }

        // Build from template
        var handle = NormalizeHandle(platform, method.HandleOrUrl);
        var url = BuildFromTemplate(platform, handle, amount, note);

        return new PaymentLink(
            method.Id,
            platform.Key,
            platform.DisplayName,
            method.DisplayLabel ?? platform.DisplayName,
            url,
            false,
            null
        );
    }

    public async Task<IReadOnlyList<PaymentLink>> BuildManyAsync(IEnumerable<OwnerPayoutMethod> methods, decimal amount, string note)
    {
        var list = methods.ToList();

        var missing = list.Where(m => m.Platform == null).Select(m => m.Id).ToList();
        if (missing.Count > 0)
        {
            var loaded = await _db.OwnerPayoutMethods
                .Where(m => missing.Contains(m.Id))
                .Include(m => m.Platform)
                .ToListAsync();

            var map = loaded.ToDictionary(m => m.Id);
            for (int i = 0; i < list.Count; i++)
                if (list[i].Platform == null && map.TryGetValue(list[i].Id, out var repl))
                    list[i] = repl;
        }

        var results = new List<PaymentLink>(list.Count);
        foreach (var m in list)
            results.Add(await BuildAsync(m, amount, note));
        return results;
    }

    #region Helpers

    private static string BuildFromTemplate(PayoutPlatform platform, string handle, decimal amount, string note)
    {
        var template = platform.LinkTemplate ?? "{handle}";

        string amt = platform.SupportsAmount ? amount.ToString("0.00", CultureInfo.InvariantCulture) : "";
        string encodedHandle = Uri.EscapeDataString(handle);
        string encodedNote = platform.SupportsNote ? Uri.EscapeDataString(note ?? string.Empty) : "";

        // Replace placeholders
        template = template
            .Replace("{handle}", encodedHandle, StringComparison.Ordinal)
            .Replace("${handle}", "$" + encodedHandle, StringComparison.Ordinal)
            .Replace("{amount}", amt, StringComparison.Ordinal)
            .Replace("{note}", encodedNote, StringComparison.Ordinal);

        // Clean query params left empty (e.g., "&note=" or "?note=")
        template = Regex.Replace(template, @"([?&])[A-Za-z0-9._~-]+=(?=(&|$))", m => m.Groups[1].Value == "?" ? "?" : "");

        // Collapse ?& → ?, && → &, and trim trailing ?/&
        template = template.Replace("?&", "?", StringComparison.Ordinal)
                           .Replace("&&", "&", StringComparison.Ordinal)
                           .TrimEnd('?', '&', '/');

        return template;
    }

    private static string NormalizeHandle(PayoutPlatform platform, string raw)
    {
        var s = (raw ?? string.Empty).Trim();

        if (!string.IsNullOrEmpty(platform.PrefixToStrip))
        {
            if (s.StartsWith(platform.PrefixToStrip, StringComparison.OrdinalIgnoreCase))
                s = s[platform.PrefixToStrip.Length..];
        }

        if (!string.IsNullOrWhiteSpace(platform.HandlePattern))
        {
            if (!Regex.IsMatch(s, platform.HandlePattern))
                throw new ArgumentException($"Handle does not match pattern for {platform.DisplayName}.");
        }

        // For Custom URL, template is "{handle}" so s must be an absolute https URL
        if (platform.Key == "custom")
        {
            if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("Custom URL must be an absolute https URL.");
            // Return the original (not encoded) so replacement keeps https:// intact
            return uri.ToString();
        }

        return s;
    }
    #endregion
}

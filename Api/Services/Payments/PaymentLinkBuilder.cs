using Api.Contracts.Payment;
using Api.Data;
using Api.Models;
using Api.Models.Owners;
using Api.Services.Payments.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Api.Services.Payments;

public sealed class PaymentLinkBuilder : IPaymentLinkBuilder
{
    private readonly LightningDbContext _db;
    private readonly ILogger<PaymentLinkBuilder> _log;

    public PaymentLinkBuilder(ILogger<PaymentLinkBuilder> log, LightningDbContext db)
    {
        _db = db;
        _log = log;
    }

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

        // Instructions-only (e.g., Zelle/Apple Cash)
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

        // Build from template (may throw on invalid handle -> caller catches)
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
        // Load missing platforms
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

        // Build safely: skip invalid methods instead of throwing
        var results = new List<PaymentLink>(list.Count);
        foreach (var m in list)
        {
            try
            {
                var link = await BuildAsync(m, amount, note);
                if (link is not null) results.Add(link);
            }
            catch (ArgumentException ex)
            {
                _log.LogWarning(ex,
                    "Skipping payout method {MethodId} for platform {PlatformKey} due to validation error (handle/url).",
                    m.Id, m.Platform?.Key);
            }
            catch (Exception ex)
            {
                _log.LogError(ex,
                    "Error building link for payout method {MethodId} (platform {PlatformKey}); skipping.",
                    m.Id, m.Platform?.Key);
            }
        }
        return results;
    }

    #region Helpers

    private static string BuildFromTemplate(PayoutPlatform platform, string handle, decimal amount, string note)
    {
        var template = platform.LinkTemplate ?? "{handle}";

        // Custom: return the validated absolute https URL unchanged.
        if (platform.Key == "custom")
            return handle;

        // ✅ If the template adds a literal prefix like "$" via "${handle}",
        // make sure the incoming handle doesn't already start with that.
        if (template.Contains("${handle}", StringComparison.Ordinal))
        {
            handle = handle.TrimStart(' ', '+');
            if (handle.StartsWith("$")) handle = handle[1..];
            else if (handle.StartsWith("%24", StringComparison.OrdinalIgnoreCase)) handle = handle[3..];
        }

        string amt = platform.SupportsAmount
            ? amount.ToString("0.00", CultureInfo.InvariantCulture)
            : "";

        string encodedHandle = WebUtility.UrlEncode(handle);
        string encodedNote = platform.SupportsNote
            ? WebUtility.UrlEncode(note ?? string.Empty)
            : "";

        template = template
            .Replace("{handle}", encodedHandle, StringComparison.Ordinal)
            .Replace("${handle}", "$" + encodedHandle, StringComparison.Ordinal)
            .Replace("{amount}", amt, StringComparison.Ordinal)
            .Replace("{note}", encodedNote, StringComparison.Ordinal);

        // Remove empty query params, collapse, trim
        template = Regex.Replace(template, @"([?&])[A-Za-z0-9._~-]+=(?=(&|$))",
                                 m => m.Groups[1].Value == "?" ? "?" : "");
        template = template.Replace("?&", "?", StringComparison.Ordinal)
                           .Replace("&&", "&", StringComparison.Ordinal)
                           .TrimEnd('?', '&', '/');

        return template;
    }




    private static string NormalizeHandle(PayoutPlatform platform, string raw)
    {
        var s = (raw ?? string.Empty).Trim();

        // Strip repeated copies of the logical prefix (e.g., "$") AND its URL-encoded form (e.g., "%24")
        if (!string.IsNullOrEmpty(platform.PrefixToStrip))
        {
            var p = platform.PrefixToStrip;
            var enc = WebUtility.UrlEncode(p); // "$" -> "%24"

            // Remove leading '+' that might have come from accidental copies or URL-encoded spaces
            while (s.Length > 0 && (s[0] == '+' || s[0] == ' ')) s = s[1..];

            // Strip ALL leading raw prefixes
            while (s.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                s = s[p.Length..];

            // Strip ALL leading encoded prefixes (e.g., "%24username")
            while (enc.Length > 0 && s.StartsWith(enc, StringComparison.OrdinalIgnoreCase))
                s = s[enc.Length..];

            // Re-trim in case we exposed spaces again
            s = s.TrimStart();
        }

        if (!string.IsNullOrWhiteSpace(platform.HandlePattern) &&
            !Regex.IsMatch(s, platform.HandlePattern))
        {
            throw new ArgumentException($"Handle does not match pattern for {platform.DisplayName}.");
        }

        // 'custom' expects a full https URL in HandleOrUrl
        if (platform.Key == "custom")
        {
            if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("Custom URL must be an absolute https URL.");
            return uri.ToString();
        }

        return s;
    }

    #endregion
}

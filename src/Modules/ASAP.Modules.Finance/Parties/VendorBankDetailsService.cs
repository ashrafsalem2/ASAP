using ASAP.Modules.Finance.Payments;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Finance.Parties;

/// <summary>
/// Records where a vendor is paid.
/// </summary>
/// <remarks>
/// Its own small service rather than a field on the vendor form, because a change here is the
/// change fraud begins with. Every change is logged with who made it and what it replaced, and an
/// IBAN a bank would refuse is refused here rather than discovered when a payment bounces.
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="user">Says who changed it.</param>
/// <param name="logger">Records every change.</param>
public sealed class VendorBankDetailsService(
    AsapDbContext context,
    IMessageCatalog messages,
    IUserContext user,
    ILogger<VendorBankDetailsService> logger)
{
    /// <summary>Sets a vendor's bank details.</summary>
    /// <param name="vendorNo">The vendor.</param>
    /// <param name="iban">The IBAN, in any spacing or case, or null to clear it.</param>
    /// <param name="bic">Their bank's BIC, where known.</param>
    /// <param name="bankName">Their bank's name.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The vendor, or why the details were refused.</returns>
    public async Task<Result<Vendor>> SetAsync(
        string vendorNo,
        string? iban,
        string? bic,
        string? bankName,
        CancellationToken cancellationToken = default)
    {
        var no = vendorNo?.Trim().ToUpperInvariant() ?? string.Empty;

        var vendor = await context.Set<Vendor>()
            .FirstOrDefaultAsync(v => v.No == no, cancellationToken)
            .ConfigureAwait(false);

        if (vendor is null)
        {
            return Result<Vendor>.Failure(messages.Render(
                FinanceMessages.NoSuchParty,
                Args(("PartyNo", no), ("PartyKind", "vendor"))));
        }

        var normalised = Iban.Normalise(iban);

        if (normalised.Length > 0 && !Iban.IsValid(normalised))
        {
            return Result<Vendor>.Failure(messages.Render(
                FinanceMessages.IbanRejected,
                Args(("Iban", iban?.Trim()))));
        }

        var previous = vendor.Iban;

        vendor.Iban = normalised.Length == 0 ? null : normalised;
        vendor.Bic = string.IsNullOrWhiteSpace(bic) ? null : bic.Trim().ToUpperInvariant();
        vendor.BankName = string.IsNullOrWhiteSpace(bankName) ? null : bankName.Trim();

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (!string.Equals(previous, vendor.Iban, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Bank details for vendor {VendorNo} changed from {PreviousIban} to {Iban} by {User}.",
                vendor.No,
                previous ?? "(none)",
                vendor.Iban ?? "(none)",
                user.UserName);
        }

        return Result<Vendor>.Success(vendor);
    }

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in pairs)
        {
            arguments[key] = value;
        }

        return arguments;
    }
}

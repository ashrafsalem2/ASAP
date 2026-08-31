using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Finance.Parties;

/// <summary>A customer group as somebody asks for it to be saved.</summary>
/// <param name="Code">Its code.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="Description">What sort of customer belongs in it.</param>
/// <param name="IsActive">Whether customers may still be put in it.</param>
public sealed record CustomerGroupRequest(
    string Code,
    string Name,
    string? NameArabic = null,
    string? Description = null,
    bool IsActive = true);

/// <summary>
/// Keeps the customer groups, and says which one a customer is in.
/// </summary>
/// <remarks>
/// A small thing that two other modules were already written against. Offers could be limited to a
/// group and price lists were about to be, and neither had anything to match on -- so every
/// group-limited offer silently applied to nobody, which is the worst way for a feature to be
/// missing: it looks configured and does nothing.
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="tenancy">Says which company this is.</param>
/// <param name="logger">Records what changed.</param>
public sealed class CustomerGroupService(
    AsapDbContext context,
    IMessageCatalog messages,
    ITenantContext tenancy,
    ILogger<CustomerGroupService> logger)
{
    /// <summary>Every group.</summary>
    /// <param name="activeOnly">Whether to leave out the ones switched off.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The groups, by code.</returns>
    public async Task<IReadOnlyList<CustomerGroup>> ListAsync(
        bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var query = context.Set<CustomerGroup>().AsNoTracking();

        if (activeOnly)
        {
            query = query.Where(g => g.IsActive);
        }

        return await query
            .OrderBy(g => g.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Writes a group.</summary>
    /// <param name="request">The group.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The saved group, or why it could not be saved.</returns>
    public async Task<Result<CustomerGroup>> SaveAsync(
        CustomerGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (code.Length == 0 || string.IsNullOrWhiteSpace(request.Name))
        {
            return Result<CustomerGroup>.Failure(messages.Render(
                FinanceMessages.CustomerGroupNeedsACodeAndName,
                Args(("Code", code))));
        }

        var group = await context.Set<CustomerGroup>()
            .FirstOrDefaultAsync(g => g.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (group is null)
        {
            group = new CustomerGroup
            {
                TenantId = tenancy.TenantId ?? Guid.Empty,
                CompanyId = tenancy.RequireCompanyId(),
                Code = code,
                Name = request.Name,
            };

            context.Set<CustomerGroup>().Add(group);
        }

        group.Name = request.Name;
        group.NameArabic = request.NameArabic;
        group.Description = request.Description;
        group.IsActive = request.IsActive;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<CustomerGroup>.Success(group);
    }

    /// <summary>
    /// Puts a customer in a group, or takes them out of one.
    /// </summary>
    /// <remarks>
    /// A group that has been switched off cannot be assigned to anybody new. What it must not do
    /// is fall out from under the customers already in it: they stay where they are, because a
    /// customer silently leaving a group is a customer silently losing a price.
    /// </remarks>
    /// <param name="customerNo">The customer.</param>
    /// <param name="groupCode">The group, or null to take them out of whatever they are in.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Whether it was saved, or why not.</returns>
    public async Task<Result> AssignAsync(
        string customerNo,
        string? groupCode,
        CancellationToken cancellationToken = default)
    {
        var no = customerNo?.Trim().ToUpperInvariant() ?? string.Empty;

        var party = await context.Set<Customer>()
            .FirstOrDefaultAsync(c => c.No == no, cancellationToken)
            .ConfigureAwait(false);

        if (party is null)
        {
            return Result.Failure(messages.Render(
                FinanceMessages.PartyNotFound,
                Args(("PartyNo", no))));
        }

        if (string.IsNullOrWhiteSpace(groupCode))
        {
            party.CustomerGroupCode = null;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success();
        }

        var code = groupCode.Trim().ToUpperInvariant();

        var group = await context.Set<CustomerGroup>()
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (group is null)
        {
            return Result.Failure(messages.Render(
                FinanceMessages.CustomerGroupNotFound,
                Args(("GroupCode", code))));
        }

        if (!group.IsActive)
        {
            return Result.Failure(messages.Render(
                FinanceMessages.CustomerGroupWithdrawn,
                Args(("GroupCode", code), ("Name", group.Name))));
        }

        party.CustomerGroupCode = group.Code;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Customer {CustomerNo} is now in group {GroupCode}.", no, code);

        return Result.Success();
    }

    /// <summary>
    /// Which group a customer is in.
    /// </summary>
    /// <param name="customerNo">The customer.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The group code, or null where they are in none.</returns>
    public async Task<string?> GroupOfAsync(
        string customerNo,
        CancellationToken cancellationToken = default)
    {
        var no = customerNo?.Trim().ToUpperInvariant() ?? string.Empty;

        if (no.Length == 0)
        {
            return null;
        }

        return await context.Set<Customer>()
            .AsNoTracking()
            .Where(c => c.No == no)
            .Select(static c => c.CustomerGroupCode)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
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

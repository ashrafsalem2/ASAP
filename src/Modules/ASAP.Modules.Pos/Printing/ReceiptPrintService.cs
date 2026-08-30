using ASAP.Modules.Pos.Receipts;
using ASAP.Modules.Pos.Sessions;
using ASAP.Modules.Pos.Stations;
using ASAP.Platform.Core.Printing;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Pos.Printing;

/// <summary>A rendered document, ready to go to paper.</summary>
/// <param name="TemplateCode">The template it came out of.</param>
/// <param name="WidthInCharacters">How wide the paper is, so a preview can show it truthfully.</param>
/// <param name="Text">The rendered text.</param>
public readonly record struct PrintedDocument(
    string TemplateCode,
    int WidthInCharacters,
    string Text);

/// <summary>
/// Turns a receipt into something a printer can take.
/// </summary>
/// <remarks>
/// <para>
/// The values a template can use are assembled here and nowhere else, which is what lets the
/// editor tell somebody which fields exist. A template language whose fields are whatever the
/// last developer happened to pass in is one nobody can write against.
/// </para>
/// <para>
/// Nothing here talks to a printer. The rendered text goes back to whoever asked, and how it
/// reaches paper — a browser print dialog, a bridge agent on the counter, an email — is a
/// separate question with more than one right answer.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="tenantContext">Supplies the branch, for choosing a branch's own template.</param>
public sealed class ReceiptPrintService(
    AsapDbContext context,
    IMessageCatalog messages,
    ITenantContext tenantContext)
{
    /// <summary>
    /// Renders a receipt.
    /// </summary>
    /// <param name="receiptNo">The receipt.</param>
    /// <param name="templateCode">A template to use, or null for the one the till would use.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The rendered document, or why it could not be rendered.</returns>
    public async Task<Result<PrintedDocument>> ReceiptAsync(
        string receiptNo,
        string? templateCode = null,
        CancellationToken cancellationToken = default)
    {
        var template = await ChooseAsync(templateCode, PrintTemplateKind.Receipt, cancellationToken)
            .ConfigureAwait(false);

        if (template is null)
        {
            return Result<PrintedDocument>.Failure(messages.Render(
                PosMessages.NoPrintTemplate,
                Args(("Kind", nameof(PrintTemplateKind.Receipt)))));
        }

        return await PreviewAsync(receiptNo, template, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Renders a receipt through a template that need not have been saved.
    /// </summary>
    /// <remarks>
    /// The editor renders against a real receipt rather than an invented one. A layout that looks
    /// right beside made-up figures is how a receipt ships with a total column too narrow for
    /// four digits.
    /// </remarks>
    /// <param name="receiptNo">The receipt.</param>
    /// <param name="template">The template to use.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The rendered document, or why it could not be rendered.</returns>
    public async Task<Result<PrintedDocument>> PreviewAsync(
        string receiptNo,
        PrintTemplate template,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);

        var receipt = await context.Set<PosReceipt>()
            .AsNoTracking()
            .Include(r => r.Lines)
            .Include(r => r.Tenders)
            .FirstOrDefaultAsync(r => r.No == receiptNo, cancellationToken)
            .ConfigureAwait(false);

        if (receipt is null)
        {
            return Result<PrintedDocument>.Failure(messages.Render(
                PosMessages.ReceiptNotFound,
                Args(("ReceiptNo", receiptNo))));
        }

        var station = await context.Set<PosStation>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Code == receipt.StationCode, cancellationToken)
            .ConfigureAwait(false);

        var document = Args(
            ("ReceiptNo", receipt.No),
            ("StationCode", receipt.StationCode),
            ("StationName", station?.Name),
            ("CustomerNo", receipt.CustomerNo),
            ("CustomerName", receipt.CustomerName),
            ("LocationCode", receipt.LocationCode),
            ("BusinessDate", receipt.BusinessDate),
            ("TakenAtUtc", receipt.TakenAtUtc),
            ("NetAmount", receipt.NetAmount),
            ("DiscountAmount", receipt.DiscountAmount),
            ("TaxAmount", receipt.TaxAmount),
            ("RoundingAmount", receipt.RoundingAmount),
            ("TotalAmount", receipt.TotalAmount),
            ("ChangeGiven", receipt.ChangeGiven),
            ("LineCount", receipt.Lines.Count));

        var lines = receipt.Lines
            .OrderBy(static l => l.LineNo)
            .Select(line => (IReadOnlyDictionary<string, object?>)Args(
                ("LineNo", line.LineNo),
                ("ItemNo", line.ItemNo),
                ("AccountNo", line.AccountNo),
                ("Description", line.Description),
                ("Quantity", line.Quantity),

                // What was actually scanned, beside what left the shelf. A receipt may print
                // either: twelve lamps is what the customer carries out, one case is what they
                // handed over, and both are true.
                ("UnitCode", line.UnitCode),
                ("QuantityRung", line.QuantityPerUnit == 0m
                    ? line.Quantity
                    : line.Quantity / line.QuantityPerUnit),
                ("UnitPrice", line.UnitPrice),
                ("DiscountPercent", line.DiscountPercent),
                ("OfferCode", line.OfferCode),
                ("OfferDiscountAmount", line.OfferDiscountAmount),
                ("LineAmount", line.LineAmount)))
            .ToList();

        var tenders = receipt.Tenders
            .Select(tender => (IReadOnlyDictionary<string, object?>)Args(
                ("Kind", tender.Kind.ToString()),
                ("Amount", tender.Amount),
                ("Reference", tender.Reference)))
            .ToList();

        var regions = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["lines"] = lines,
            ["tenders"] = tenders,
        };

        return Result<PrintedDocument>.Success(new PrintedDocument(
            template.Code,
            template.WidthInCharacters,
            PrintTemplateRenderer.Render(template.Content, document, regions)));
    }

    /// <summary>
    /// The fields a template of a given kind may use.
    /// </summary>
    /// <remarks>
    /// Assembled from the same place the values are, so the list cannot drift out of step with
    /// what actually renders. The editor shows it beside the template, which is the difference
    /// between writing a layout and guessing at one.
    /// </remarks>
    /// <param name="kind">Which kind of template.</param>
    /// <returns>The field names, and the region each belongs to.</returns>
    public static IReadOnlyList<(string Region, string Field)> FieldsFor(PrintTemplateKind kind)
        => kind switch
        {
            PrintTemplateKind.Receipt =>
            [
                ("", "ReceiptNo"), ("", "StationCode"), ("", "StationName"),
                ("", "CustomerNo"), ("", "CustomerName"), ("", "LocationCode"),
                ("", "BusinessDate"), ("", "TakenAtUtc"), ("", "NetAmount"),
                ("", "DiscountAmount"), ("", "TaxAmount"), ("", "RoundingAmount"),
                ("", "TotalAmount"), ("", "ChangeGiven"), ("", "LineCount"),
                ("lines", "LineNo"), ("lines", "ItemNo"), ("lines", "AccountNo"),
                ("lines", "Description"), ("lines", "Quantity"),
                ("lines", "UnitCode"), ("lines", "QuantityRung"), ("lines", "UnitPrice"),
                ("lines", "DiscountPercent"), ("lines", "OfferCode"),
                ("lines", "OfferDiscountAmount"), ("lines", "LineAmount"),
                ("tenders", "Kind"), ("tenders", "Amount"), ("tenders", "Reference"),
            ],
            _ => [],
        };

    /// <summary>
    /// Chooses a template: the one named, else this branch's, else the company's.
    /// </summary>
    private async Task<PrintTemplate?> ChooseAsync(
        string? templateCode,
        PrintTemplateKind kind,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(templateCode))
        {
            return await context.Set<PrintTemplate>()
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Code == templateCode, cancellationToken)
                .ConfigureAwait(false);
        }

        var candidates = await context.Set<PrintTemplate>()
            .AsNoTracking()
            .Where(t => t.Kind == kind && t.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // A branch's own beats the company's. That is the whole reason the field exists: a shop
        // wanting its own telephone number at the bottom should not need its own installation.
        return candidates.Find(t => t.BranchId == tenantContext.BranchId && t.IsDefault)
               ?? candidates.Find(t => t.BranchId == tenantContext.BranchId)
               ?? candidates.Find(static t => t.BranchId is null && t.IsDefault)
               ?? candidates.Find(static t => t.BranchId is null);
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

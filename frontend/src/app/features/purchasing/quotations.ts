import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  QuotationComparisonRow,
  QuotationRequest,
  QuotationRequestSummary,
} from '../../core/api/asap-api.models';
import { AuthService } from '../../core/auth/auth.service';
import { PurchasingService } from '../../core/api/purchasing.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/** One line being typed onto a new request. */
interface DraftLine {
  no: string;
  description: string;
  quantity: number | null;
}

/** What one vendor is being recorded as having said about one line. */
interface QuoteEntry {
  price: number | null;
  leadTime: number | null;
}

/**
 * Asking several vendors the same question, and choosing between the answers.
 *
 * The comparison is the whole screen. Cheapest and fastest are flagged separately because they are
 * usually different vendors, and a table showing only money would make the choice look obvious when
 * it is not. Awarding anything other than the cheapest quote is refused without a reason — not
 * because the dearer supplier is wrong, but because that is the decision somebody asks about a year
 * later.
 */
@Component({
  selector: 'asap-quotations',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './quotations.html',
})
export class Quotations implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(PurchasingService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly requests = signal<QuotationRequestSummary[]>([]);
  protected readonly selected = signal<QuotationRequest | null>(null);
  protected readonly drafting = signal(false);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);

  protected statusFilter = '';
  protected description = '';
  protected locationCode = '';
  protected respondBy = '';
  protected draftLines: DraftLine[] = [{ no: '', description: '', quantity: null }];

  protected inviteVendors = '';
  protected quoteVendorNo = '';
  protected declineReason = '';
  protected orderVendorNo = '';

  private readonly quoteEntries = new Map<number, QuoteEntry>();
  private readonly awardReasons = new Map<number, string>();
  private readonly awardVendors = new Map<number, string>();

  async ngOnInit(): Promise<void> {
    try {
      await this.refresh();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected canWrite(): boolean {
    return this.auth.can('Purchasing.Quotation.Update');
  }

  protected canOrder(): boolean {
    return this.auth.can('Purchasing.Order.Create');
  }

  protected async refresh(): Promise<void> {
    this.requests.set(await this.api.quotationRequests(this.statusFilter || undefined));
  }

  protected startNew(): void {
    this.drafting.set(true);
    this.selected.set(null);
    this.description = '';
    this.locationCode = '';
    this.respondBy = '';
    this.draftLines = [{ no: '', description: '', quantity: null }];
  }

  protected cancelDraft(): void {
    this.drafting.set(false);
  }

  protected addLine(): void {
    this.draftLines = [...this.draftLines, { no: '', description: '', quantity: null }];
  }

  protected removeLine(index: number): void {
    this.draftLines = this.draftLines.filter((_, at) => at !== index);
  }

  protected trackLine(index: number): number {
    return index;
  }

  protected async save(): Promise<void> {
    const lines = this.draftLines
      .filter((line) => line.no.trim() && (line.quantity ?? 0) > 0)
      .map((line) => ({
        type: 'Item',
        no: line.no.trim(),
        quantity: line.quantity ?? 0,
        description: line.description.trim() || null,
      }));

    if (lines.length === 0) {
      return;
    }

    await this.run('save', async () => {
      const created = await this.api.createQuotationRequest({
        lines,
        locationCode: this.locationCode || null,
        respondByDate: this.respondBy || null,
        description: this.description || null,
      });

      this.messages.showSuccess(this.t('purchasing.quotations.saved', { No: created.no }));
      this.drafting.set(false);
      this.selected.set(created);
    });
  }

  protected async open(summary: QuotationRequestSummary): Promise<void> {
    this.drafting.set(false);
    this.quoteEntries.clear();
    this.awardReasons.clear();
    this.awardVendors.clear();

    try {
      this.selected.set(await this.api.quotationRequest(summary.no));
    } catch (error) {
      this.messages.showError(error);
    }
  }

  protected async invite(): Promise<void> {
    const vendors = this.inviteVendors
      .split(',')
      .map((v) => v.trim())
      .filter((v) => v.length > 0);

    if (vendors.length === 0) {
      return;
    }

    await this.act('invite', (no) => this.api.inviteQuotationVendors(no, vendors), () => {
      this.inviteVendors = '';
    });
  }

  protected async send(): Promise<void> {
    await this.act('send', (no) => this.api.sendQuotationRequest(no));
  }

  protected quotePriceOf(lineNo: number): number | null {
    return this.quoteEntries.get(lineNo)?.price ?? null;
  }

  protected setQuotePrice(lineNo: number, value: number | null): void {
    const entry = this.quoteEntries.get(lineNo) ?? { price: null, leadTime: null };
    this.quoteEntries.set(lineNo, { ...entry, price: value });
  }

  protected quoteLeadTimeOf(lineNo: number): number | null {
    return this.quoteEntries.get(lineNo)?.leadTime ?? null;
  }

  protected setQuoteLeadTime(lineNo: number, value: number | null): void {
    const entry = this.quoteEntries.get(lineNo) ?? { price: null, leadTime: null };
    this.quoteEntries.set(lineNo, { ...entry, leadTime: value });
  }

  protected async recordQuote(): Promise<void> {
    const request = this.selected();

    if (!request || !this.quoteVendorNo.trim()) {
      return;
    }

    const lines = request.comparison
      .map((row) => ({ row, entry: this.quoteEntries.get(row.lineNo) }))
      .filter((r) => (r.entry?.price ?? null) !== null)
      .map((r) => ({
        lineNo: r.row.lineNo,
        unitPrice: r.entry!.price!,
        leadTimeDays: r.entry!.leadTime ?? null,
      }));

    if (lines.length === 0) {
      return;
    }

    await this.act(
      'quote',
      (no) => this.api.recordQuotation(no, this.quoteVendorNo.trim(), lines),
      () => {
        this.quoteEntries.clear();
        this.quoteVendorNo = '';
      },
    );
  }

  protected async decline(): Promise<void> {
    if (!this.quoteVendorNo.trim()) {
      return;
    }

    await this.act(
      'decline',
      (no) =>
        this.api.declineQuotation(no, this.quoteVendorNo.trim(), this.declineReason.trim() || undefined),
      () => {
        this.quoteVendorNo = '';
        this.declineReason = '';
      },
    );
  }

  protected awardVendorOf(lineNo: number): string {
    return this.awardVendors.get(lineNo) ?? '';
  }

  protected setAwardVendor(lineNo: number, value: string): void {
    this.awardVendors.set(lineNo, value);
  }

  protected awardReasonOf(lineNo: number): string {
    return this.awardReasons.get(lineNo) ?? '';
  }

  protected setAwardReason(lineNo: number, value: string): void {
    this.awardReasons.set(lineNo, value);
  }

  protected async award(): Promise<void> {
    const request = this.selected();

    if (!request) {
      return;
    }

    const awards = request.comparison
      .filter((row) => (this.awardVendors.get(row.lineNo) ?? '').trim().length > 0)
      .map((row) => ({
        lineNo: row.lineNo,
        vendorNo: this.awardVendors.get(row.lineNo)!.trim(),
        reason: (this.awardReasons.get(row.lineNo) ?? '').trim() || null,
      }));

    if (awards.length === 0) {
      return;
    }

    await this.act('award', (no) => this.api.awardQuotation(no, awards), () => {
      this.awardVendors.clear();
      this.awardReasons.clear();
    });
  }

  protected async raiseOrder(): Promise<void> {
    const request = this.selected();

    if (!request || !this.orderVendorNo.trim()) {
      return;
    }

    await this.run('order', async () => {
      const result = await this.api.orderFromQuotation(request.no, this.orderVendorNo.trim());

      this.messages.showSuccess(
        this.t('purchasing.quotations.ordered', {
          No: result.order.no,
          Vendor: this.orderVendorNo.trim(),
        }),
      );

      this.orderVendorNo = '';
      this.selected.set(await this.api.quotationRequest(request.no));
    });
  }

  /** Whether every line has somebody to award it to. */
  protected hasQuotes(row: QuotationComparisonRow): boolean {
    return row.quotes.length > 0;
  }

  protected statusLabel(status: string): string {
    return this.t(`purchasing.quotations.status.${status}` as TranslationKey);
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  private async act(
    what: string,
    work: (requestNo: string) => Promise<QuotationRequest>,
    after?: () => void,
  ): Promise<void> {
    const request = this.selected();

    if (!request) {
      return;
    }

    await this.run(what, async () => {
      this.selected.set(await work(request.no));
      after?.();
    });
  }

  private async run(what: string, work: () => Promise<void>): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.busy.set(what);
    this.messages.clear();

    try {
      await work();
      await this.refresh();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(null);
    }
  }
}

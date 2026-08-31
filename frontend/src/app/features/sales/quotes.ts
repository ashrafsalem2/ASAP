import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CreateSalesQuoteRequest, SalesQuote } from '../../core/api/asap-api.models';
import { AuthService } from '../../core/auth/auth.service';
import { SalesService } from '../../core/api/sales.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/** One line being typed onto a new quote. */
interface DraftLine {
  no: string;
  quantity: number | null;
  unitPrice: number | null;
  discountPercent: number | null;
}

/**
 * Prices offered to customers, and what became of them.
 *
 * Two things here are worth understanding and both are refusals rather than features. A quote runs
 * out, because a price nobody can withdraw is not a price. And accepting carries the quoted figures
 * onto the order untouched — the customer accepted the number in front of them, so an expired quote
 * is refused rather than quietly repriced.
 */
@Component({
  selector: 'asap-quotes',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './quotes.html',
})
export class Quotes implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(SalesService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly quotes = signal<SalesQuote[]>([]);
  protected readonly selected = signal<SalesQuote | null>(null);
  protected readonly drafting = signal(false);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);

  protected statusFilter = '';
  protected customerNo = '';
  protected validUntil = '';
  protected locationCode = '';
  protected declineReason = '';
  protected draftLines: DraftLine[] = [{ no: '', quantity: null, unitPrice: null, discountPercent: null }];

  /**
   * Whether every line carries a price, so a running total can mean anything.
   *
   * Leaving the price blank is the ordinary case -- it means take whatever this customer has been
   * agreed, and only the server knows that. Adding those lines up as nought would print a total
   * that is confidently wrong, so the screen says it cannot tell yet instead.
   *
   * A method rather than a computed signal: the draft lines are plain objects that ngModel writes
   * into, and a computed would track nothing and answer the same thing for ever.
   */
  protected draftIsPriced(): boolean {
    return this.draftLines.every((line) => (line.unitPrice ?? 0) > 0);
  }

  /** What the quote being typed comes to, where every line says what it costs. */
  protected draftTotal(): number {
    return this.draftLines.reduce(
      (total, line) =>
        total +
        (line.quantity ?? 0) * (line.unitPrice ?? 0) * (1 - (line.discountPercent ?? 0) / 100),
      0,
    );
  }

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
    return this.auth.can('Sales.Quote.Create');
  }

  protected canOrder(): boolean {
    return this.auth.can('Sales.Order.Create');
  }

  protected async refresh(): Promise<void> {
    this.quotes.set(await this.api.quotes(this.statusFilter || undefined));
  }

  protected startNew(): void {
    this.drafting.set(true);
    this.selected.set(null);
    this.customerNo = '';
    this.validUntil = '';
    this.locationCode = '';
    this.draftLines = [{ no: '', quantity: null, unitPrice: null, discountPercent: null }];
  }

  protected cancel(): void {
    this.drafting.set(false);
  }

  protected addLine(): void {
    this.draftLines = [
      ...this.draftLines,
      { no: '', quantity: null, unitPrice: null, discountPercent: null },
    ];
  }

  protected removeLine(index: number): void {
    this.draftLines = this.draftLines.filter((_, at) => at !== index);
  }

  protected trackLine(index: number): number {
    return index;
  }

  protected async save(): Promise<void> {
    if (this.busy() || !this.customerNo.trim()) {
      return;
    }

    const lines = this.draftLines
      .filter((line) => line.no.trim() && (line.quantity ?? 0) > 0)
      .map((line) => ({
        type: 'Item',
        no: line.no.trim(),
        quantity: line.quantity ?? 0,

        // Nought means take whatever this customer has been agreed, which is the ordinary case.
        unitPrice: line.unitPrice ?? 0,
        discountPercent: line.discountPercent ?? 0,
      }));

    if (lines.length === 0) {
      return;
    }

    const request: CreateSalesQuoteRequest = {
      customerNo: this.customerNo.trim(),
      lines,
      validUntil: this.validUntil || null,
      locationCode: this.locationCode || null,
    };

    this.busy.set('save');
    this.messages.clear();

    try {
      const result = await this.api.createQuote(request);

      this.report(result.messages);
      this.messages.showSuccess(
        this.t('sales.quotes.saved', { No: result.quote.no, Until: result.quote.validUntil }),
      );

      this.drafting.set(false);
      await this.refresh();
      this.selected.set(result.quote);
    } catch (error) {
      this.messages.showError(error, this.t('sales.quotes.save'));
    } finally {
      this.busy.set(null);
    }
  }

  protected async open(quote: SalesQuote): Promise<void> {
    this.drafting.set(false);
    this.declineReason = '';

    try {
      this.selected.set(await this.api.quote(quote.no));
    } catch (error) {
      this.messages.showError(error);
    }
  }

  protected async send(): Promise<void> {
    await this.act('send', async (no) => {
      this.selected.set(await this.api.sendQuote(no));
    });
  }

  protected async accept(): Promise<void> {
    const quote = this.selected();

    if (!quote || this.busy()) {
      return;
    }

    this.busy.set('accept');
    this.messages.clear();

    try {
      const result = await this.api.acceptQuote(quote.no);

      this.report(result.messages);
      this.messages.showSuccess(
        this.t('sales.quotes.accepted', { No: quote.no, OrderNo: result.order.no }),
      );

      await this.refresh();
      this.selected.set(await this.api.quote(quote.no));
    } catch (error) {
      this.messages.showError(error, this.t('sales.quotes.accept'));
    } finally {
      this.busy.set(null);
    }
  }

  protected async decline(): Promise<void> {
    await this.act('decline', async (no) => {
      this.selected.set(await this.api.declineQuote(no, this.declineReason.trim() || undefined));
      this.messages.showSuccess(this.t('sales.quotes.declined'));
    });
  }

  protected async expire(): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.busy.set('expire');

    try {
      const result = await this.api.expireQuotes();

      this.messages.showSuccess(this.t('sales.quotes.expired', { Count: result.expired }));
      await this.refresh();
    } catch (error) {
      this.messages.showError(error, this.t('sales.quotes.expire'));
    } finally {
      this.busy.set(null);
    }
  }

  protected statusLabel(status: string): string {
    return this.t(`sales.quotes.status.${status}` as TranslationKey);
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  private async act(what: string, run: (quoteNo: string) => Promise<void>): Promise<void> {
    const quote = this.selected();

    if (!quote || this.busy()) {
      return;
    }

    this.busy.set(what);
    this.messages.clear();

    try {
      await run(quote.no);
      await this.refresh();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(null);
    }
  }

  private report(messages: { code: string }[] | undefined): void {
    for (const message of messages ?? []) {
      this.messages.show(message as never);
    }
  }
}

import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { PurchaseOrder, PurchaseOrderLine } from '../../core/api/asap-api.models';
import { AsapMessage } from '../../core/api/asap-message';
import { PurchaseLineQuantity, PurchasingService } from '../../core/api/purchasing.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/** What the user has keyed against one line, for whichever step they are performing. */
interface LineEntry {
  quantity: number | null;
  price: number | null;
}

/**
 * One purchase order, and the two steps that post against it.
 *
 * Receiving and invoicing are separate panels rather than one form, because they are separate
 * permissions held by different people: the storeman who signs for a delivery is not the person
 * who agrees the company should pay for it. Each panel appears only while there is something for
 * it to do.
 */
@Component({
  selector: 'asap-purchase-order',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink],
  templateUrl: './purchase-order.html',
  styleUrl: './purchasing.scss',
})
export class PurchaseOrderDetail implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly purchasing = inject(PurchasingService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);
  private readonly route = inject(ActivatedRoute);

  protected readonly order = signal<PurchaseOrder | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);

  protected orderNo = '';
  protected deliveryNo = '';
  protected invoiceNo = '';
  protected returnReason = '';

  /**
   * What has been keyed per line, kept outside the order so reloading it does not discard a
   * half-typed receipt.
   */
  private readonly receiptEntries = new Map<number, LineEntry>();
  private readonly invoiceEntries = new Map<number, LineEntry>();
  private readonly returnEntries = new Map<number, LineEntry>();

  protected readonly canReceiveAnything = computed(
    () => (this.order()?.lines ?? []).some((line) => line.outstandingToReceive > 0),
  );

  protected readonly canInvoiceAnything = computed(
    () => (this.order()?.lines ?? []).some((line) => line.receivedNotInvoiced > 0),
  );

  /**
   * Whether anything could still go back to the vendor.
   *
   * Received, not invoiced: goods can go back before their invoice ever turns up, and rejecting a
   * faulty delivery at the door is the ordinary case rather than the exception.
   */
  protected readonly canReturnAnything = computed(
    () => (this.order()?.lines ?? []).some((line) => line.returnableQuantity > 0),
  );

  async ngOnInit(): Promise<void> {
    this.orderNo = this.route.snapshot.paramMap.get('orderNo') ?? '';
    await this.load();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canReceive(): boolean {
    return this.auth.can('Purchasing.Receipt.Post');
  }

  protected canInvoice(): boolean {
    return this.auth.can('Purchasing.Invoice.Post');
  }

  protected canReturn(): boolean {
    return this.auth.can('Purchasing.Return.Post');
  }

  protected canRelease(): boolean {
    return this.auth.can('Purchasing.Order.Create') && this.order()?.status === 'Open';
  }

  protected quantity(value: number): string {
    return new Intl.NumberFormat(this.i18n.locale(), { maximumFractionDigits: 5 }).format(value);
  }

  protected receiptOf(line: PurchaseOrderLine): number | null {
    return this.receiptEntries.get(line.lineNo)?.quantity ?? null;
  }

  protected setReceipt(line: PurchaseOrderLine, value: number | null): void {
    this.receiptEntries.set(line.lineNo, { quantity: value, price: null });
  }

  protected returnQuantityOf(line: PurchaseOrderLine): number | null {
    return this.returnEntries.get(line.lineNo)?.quantity ?? null;
  }

  protected setReturnQuantity(line: PurchaseOrderLine, value: number | null): void {
    this.returnEntries.set(line.lineNo, { quantity: value, price: null });
  }

  protected invoiceQuantityOf(line: PurchaseOrderLine): number | null {
    return this.invoiceEntries.get(line.lineNo)?.quantity ?? null;
  }

  protected invoicePriceOf(line: PurchaseOrderLine): number | null {
    return this.invoiceEntries.get(line.lineNo)?.price ?? null;
  }

  protected setInvoiceQuantity(line: PurchaseOrderLine, value: number | null): void {
    const current = this.invoiceEntries.get(line.lineNo) ?? { quantity: null, price: null };
    this.invoiceEntries.set(line.lineNo, { ...current, quantity: value });
  }

  protected setInvoicePrice(line: PurchaseOrderLine, value: number | null): void {
    const current = this.invoiceEntries.get(line.lineNo) ?? { quantity: null, price: null };
    this.invoiceEntries.set(line.lineNo, { ...current, price: value });
  }

  protected async release(): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.messages.clear();
    this.busy.set('release');

    try {
      const updated = await this.purchasing.release(this.orderNo);

      this.order.set(updated);
      this.messages.showSuccess(this.t('purchasing.orders.releasedNow', { No: this.orderNo }));
    } catch (error) {
      this.messages.showError(error, this.t('purchasing.orders.release'));
    } finally {
      this.busy.set(null);
    }
  }

  protected async receive(): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.messages.clear();
    this.busy.set('receive');

    try {
      const result = await this.purchasing.receive(
        this.orderNo,

        // Nothing keyed means everything outstanding, which is the ordinary case.
        this.linesFrom(this.receiptEntries, (line) => line.outstandingToReceive),
        this.deliveryNo || undefined,
      );

      this.report(result.messages);
      this.messages.showSuccess(
        this.t(
          result.lineCount === 1 ? 'purchasing.receipt.doneOne' : 'purchasing.receipt.done',
          {
            Count: result.lineCount,
            Value: this.i18n.total(result.value),
            Transaction: result.transactionNo,
          },
        ),
      );

      this.receiptEntries.clear();
      this.deliveryNo = '';

      await this.load();
    } catch (error) {
      this.messages.showError(error, this.t('purchasing.receipt.action'));
    } finally {
      this.busy.set(null);
    }
  }

  /**
   * Sends goods back to the vendor.
   *
   * The credit memo covers only the part that had been invoiced. Goods returned before their
   * invoice arrives unwind the accrual and stop there, because there is no debt to reverse.
   */
  protected async sendBack(): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.messages.clear();
    this.busy.set('return');

    try {
      const result = await this.purchasing.sendBack(
        this.orderNo,
        this.linesFrom(this.returnEntries, (line) => line.returnableQuantity),
        this.returnReason.trim() || undefined,
      );

      this.report(result.messages);
      this.messages.showSuccess(
        result.creditMemoNo
          ? this.t('purchasing.return.done', {
              No: result.creditMemoNo,
              Total: this.i18n.total(result.totalAmount),
            })
          : this.t('purchasing.return.doneUninvoiced', {
              Cost: this.i18n.total(Math.abs(result.costAmount)),
            }),
      );

      this.returnEntries.clear();
      this.returnReason = '';

      await this.load();
    } catch (error) {
      this.messages.showError(error, this.t('purchasing.return.action'));
    } finally {
      this.busy.set(null);
    }
  }

  protected async invoice(): Promise<void> {
    if (this.busy()) {
      return;
    }

    if (!this.invoiceNo.trim()) {
      // Their number is how anybody finds this again when the vendor telephones, so it is asked
      // for here rather than left to the server to refuse.
      this.messages.showError(
        new Error(this.t('purchasing.invoice.needsNumber')),
        this.t('purchasing.invoice.action'),
      );

      return;
    }

    this.messages.clear();
    this.busy.set('invoice');

    try {
      const result = await this.purchasing.invoice(
        this.orderNo,
        this.invoiceNo.trim(),
        this.linesFrom(this.invoiceEntries, (line) => line.receivedNotInvoiced),
      );

      this.report(result.messages);
      this.messages.showSuccess(
        this.t('purchasing.invoice.done', {
          No: result.documentNo,
          Net: this.i18n.total(result.netAmount),
          Tax: this.i18n.total(result.taxAmount),
          Total: this.i18n.total(result.totalAmount),
        }),
      );

      this.invoiceEntries.clear();
      this.invoiceNo = '';

      await this.load();
    } catch (error) {
      this.messages.showError(error, this.t('purchasing.invoice.action'));
    } finally {
      this.busy.set(null);
    }
  }

  /**
   * Shows what the server said, less its own confirmation of success.
   *
   * Posting an invoice goes through the journal engine, which reports that it wrote three entries
   * under some transaction number. That is true and it is not what somebody buying office chairs
   * came to find out -- and this screen writes a better sentence immediately afterwards. Warnings
   * and above always show: the price variance is the whole reason anybody reads these.
   */
  private report(messages: AsapMessage[] | undefined): void {
    this.messages.showAll((messages ?? []).filter((message) => message.severity !== 'Success'));
  }

  /**
   * Turns what was keyed into lines, or undefined when nothing was.
   *
   * Undefined means "everything outstanding" to the server, which is what somebody who typed
   * nothing meant. Sending an empty list instead would mean the opposite.
   *
   * A line where only the price was keyed still counts, and takes the whole outstanding quantity.
   * Typing a price alone means "bill all of it, at this price", and dropping such a line silently
   * invoiced the agreed price instead -- the user's correction ignored, and nothing said.
   */
  private linesFrom(
    entries: Map<number, LineEntry>,
    outstanding: (line: PurchaseOrderLine) => number,
  ): PurchaseLineQuantity[] | undefined {
    const lines: PurchaseLineQuantity[] = [];

    for (const line of this.order()?.lines ?? []) {
      const entry = entries.get(line.lineNo);
      const keyedQuantity = entry?.quantity ?? null;
      const keyedPrice = entry?.price ?? null;

      if ((keyedQuantity ?? 0) <= 0 && keyedPrice === null) {
        continue;
      }

      const quantity = (keyedQuantity ?? 0) > 0 ? (keyedQuantity as number) : outstanding(line);

      if (quantity > 0) {
        lines.push({
          lineNo: line.lineNo,
          quantity,
          directUnitCost: keyedPrice ?? undefined,
        });
      }
    }

    return lines.length > 0 ? lines : undefined;
  }

  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      this.order.set(await this.purchasing.order(this.orderNo));
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

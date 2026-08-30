import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { SalesOrder, SalesOrderLine } from '../../core/api/asap-api.models';
import { AsapMessage } from '../../core/api/asap-message';
import { SalesLineQuantity, SalesService } from '../../core/api/sales.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * One sales order, and the two steps that post against it.
 *
 * Shipping and invoicing are separate panels rather than one form, because they are separate
 * permissions held by different people: the storeman who sends the goods is not the person who
 * decides the customer should be billed for them. Each panel appears only while there is
 * something for it to do.
 */
@Component({
  selector: 'asap-sales-order',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink],
  templateUrl: './sales-order.html',
  styleUrl: './sales.scss',
})
export class SalesOrderDetail implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly sales = inject(SalesService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);
  private readonly route = inject(ActivatedRoute);

  protected readonly order = signal<SalesOrder | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);

  protected orderNo = '';
  protected overrideReason = '';
  protected returnReason = '';

  /**
   * What has been keyed per line, kept outside the order so reloading it does not discard a
   * half-typed despatch.
   */
  private readonly shipmentEntries = new Map<number, number | null>();
  private readonly invoiceEntries = new Map<number, number | null>();
  private readonly returnEntries = new Map<number, number | null>();

  protected readonly canShipAnything = computed(
    () => (this.order()?.lines ?? []).some((line) => line.outstandingToShip > 0),
  );

  protected readonly canInvoiceAnything = computed(
    () => (this.order()?.lines ?? []).some((line) => line.shippedNotInvoiced > 0),
  );

  /**
   * Whether anything on this order could still come back.
   *
   * Invoiced, not shipped: goods the customer was never billed for have no debt to reverse, and
   * go back by correcting the shipment rather than by a credit memo for nothing.
   */
  protected readonly canReturnAnything = computed(
    () => (this.order()?.lines ?? []).some((line) => line.returnableQuantity > 0),
  );

  /** What has been given away on this order, which a netted-down price could not answer. */
  protected readonly discountGiven = computed(() =>
    (this.order()?.lines ?? []).reduce(
      (total, line) => total + line.quantity * line.unitPrice * (line.discountPercent / 100),
      0,
    ),
  );

  async ngOnInit(): Promise<void> {
    this.orderNo = this.route.snapshot.paramMap.get('orderNo') ?? '';
    await this.load();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canShip(): boolean {
    return this.auth.can('Sales.Shipment.Post');
  }

  protected canInvoice(): boolean {
    return this.auth.can('Sales.Invoice.Post');
  }

  protected canReturn(): boolean {
    return this.auth.can('Sales.Return.Post');
  }

  protected canRelease(): boolean {
    return this.auth.can('Sales.Order.Create') && this.order()?.status === 'Open';
  }

  protected quantity(value: number): string {
    return new Intl.NumberFormat(this.i18n.locale(), { maximumFractionDigits: 5 }).format(value);
  }

  protected percent(value: number): string {
    return new Intl.NumberFormat(this.i18n.locale(), { maximumFractionDigits: 3 }).format(value);
  }

  protected shipmentOf(line: SalesOrderLine): number | null {
    return this.shipmentEntries.get(line.lineNo) ?? null;
  }

  protected setShipment(line: SalesOrderLine, value: number | null): void {
    this.shipmentEntries.set(line.lineNo, value);
  }

  protected returnQuantityOf(line: SalesOrderLine): number | null {
    return this.returnEntries.get(line.lineNo) ?? null;
  }

  protected setReturnQuantity(line: SalesOrderLine, value: number | null): void {
    this.returnEntries.set(line.lineNo, value);
  }

  protected invoiceQuantityOf(line: SalesOrderLine): number | null {
    return this.invoiceEntries.get(line.lineNo) ?? null;
  }

  protected setInvoiceQuantity(line: SalesOrderLine, value: number | null): void {
    this.invoiceEntries.set(line.lineNo, value);
  }

  protected async release(): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.messages.clear();
    this.busy.set('release');

    try {
      const updated = await this.sales.release(this.orderNo);

      this.order.set(updated);
      this.messages.showSuccess(this.t('sales.orders.releasedNow', { No: this.orderNo }));
    } catch (error) {
      this.messages.showError(error, this.t('sales.orders.release'));
    } finally {
      this.busy.set(null);
    }
  }

  protected async ship(): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.messages.clear();
    this.busy.set('ship');

    try {
      const result = await this.sales.ship(
        this.orderNo,

        // Nothing keyed means everything outstanding, which is the ordinary case.
        this.linesFrom(this.shipmentEntries),
        this.overrideReason.trim() || undefined,
      );

      this.report(result.messages);
      this.messages.showSuccess(
        this.t(result.lineCount === 1 ? 'sales.shipment.doneOne' : 'sales.shipment.done', {
          Count: result.lineCount,
          Cost: this.i18n.total(result.costAmount),
          Transaction: result.transactionNo,
        }),
      );

      this.shipmentEntries.clear();
      this.overrideReason = '';

      await this.load();
    } catch (error) {
      this.messages.showError(error, this.t('sales.shipment.action'));
    } finally {
      this.busy.set(null);
    }
  }

  protected async invoice(): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.messages.clear();
    this.busy.set('invoice');

    try {
      const result = await this.sales.invoice(
        this.orderNo,
        this.linesFrom(this.invoiceEntries),
        this.overrideReason.trim() || undefined,
      );

      this.report(result.messages);
      this.messages.showSuccess(
        this.t('sales.invoice.done', {
          No: result.documentNo,
          Net: this.i18n.total(result.netAmount),
          Tax: this.i18n.total(result.taxAmount),
          Total: this.i18n.total(result.totalAmount),
        }),
      );

      this.invoiceEntries.clear();
      this.overrideReason = '';

      await this.load();
    } catch (error) {
      this.messages.showError(error, this.t('sales.invoice.action'));
    } finally {
      this.busy.set(null);
    }
  }

  /**
   * Takes goods back and credits the customer.
   *
   * Nothing keyed means everything that could still come back, which is the ordinary case: a
   * customer returning the lot.
   */
  protected async takeBack(): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.messages.clear();
    this.busy.set('return');

    try {
      const result = await this.sales.takeBack(
        this.orderNo,
        this.linesFrom(this.returnEntries),
        this.returnReason.trim() || undefined,
        this.overrideReason.trim() || undefined,
      );

      this.report(result.messages);
      this.messages.showSuccess(
        this.t('sales.return.done', {
          No: result.creditMemoNo,
          Total: this.i18n.total(result.totalAmount),
          Cost: this.i18n.total(result.costAmount),
        }),
      );

      this.returnEntries.clear();
      this.returnReason = '';
      this.overrideReason = '';

      await this.load();
    } catch (error) {
      this.messages.showError(error, this.t('sales.return.action'));
    } finally {
      this.busy.set(null);
    }
  }

  /**
   * Shows what the server said, less its own confirmation of success.
   *
   * Posting an invoice goes through the ledger, which reports that it wrote five entries under
   * some transaction number. That is true and it is not what somebody selling office chairs came
   * to find out — and this screen writes a better sentence immediately afterwards. Warnings and
   * above always show: selling below cost, and shipping stock that is not there, are the whole
   * reason anybody reads these.
   */
  private report(messages: AsapMessage[] | undefined): void {
    this.messages.showAll((messages ?? []).filter((message) => message.severity !== 'Success'));
  }

  /**
   * Turns what was keyed into lines, or undefined when nothing was.
   *
   * Undefined means "everything outstanding" to the server, which is what somebody who typed
   * nothing meant. Sending an empty list instead would mean the opposite.
   */
  private linesFrom(entries: Map<number, number | null>): SalesLineQuantity[] | undefined {
    const lines: SalesLineQuantity[] = [];

    for (const line of this.order()?.lines ?? []) {
      const keyed = entries.get(line.lineNo) ?? null;

      if ((keyed ?? 0) <= 0) {
        continue;
      }

      lines.push({ lineNo: line.lineNo, quantity: keyed as number });
    }

    // Every line left blank on a screen where some were keyed still means "not this one", so the
    // outstanding figure is read only when the whole panel was left alone.
    return lines.length > 0 ? lines : undefined;
  }

  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      this.order.set(await this.sales.order(this.orderNo));
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

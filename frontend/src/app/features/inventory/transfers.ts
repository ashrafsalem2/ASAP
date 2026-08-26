import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  Item,
  StockLocation,
  Transfer,
  TransferLine,
  TransferMoveReceipt,
} from '../../core/api/asap-api.models';
import { InventoryService } from '../../core/api/inventory.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/** One line being drafted onto a new transfer. */
interface DraftLine {
  itemNo: string;
  quantity: number | null;
}

/**
 * Raising, shipping and receiving transfers.
 *
 * The screen follows the journey rather than the data: a transfer is raised, then shipped, then
 * received, and only the action that is actually available next is offered on each row. Showing
 * "Receive" against something that has not shipped invites a click that can only be refused.
 */
@Component({
  selector: 'asap-transfers',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './transfers.html',
  styleUrl: './transfers.scss',
})
export class Transfers implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly inventory = inject(InventoryService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly transfers = signal<Transfer[]>([]);
  protected readonly items = signal<Item[]>([]);
  protected readonly locations = signal<StockLocation[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);
  protected readonly expanded = signal<string | null>(null);

  protected fromLocationCode = '';
  protected toLocationCode = '';
  protected description = '';
  protected draft: DraftLine[] = [{ itemNo: '', quantity: null }];

  /**
   * What actually arrived, keyed by transfer and item.
   *
   * Kept out of the transfer objects themselves so that reloading the list does not discard what
   * somebody has half-typed into a receipt.
   */
  private readonly arrivals = new Map<string, number>();

  async ngOnInit(): Promise<void> {
    try {
      const [items, locations] = await Promise.all([
        this.inventory.items(),
        this.inventory.locations(),
      ]);

      this.items.set(items);
      this.locations.set(locations.filter((location) => !location.isBlocked));
    } catch (error) {
      this.messages.showError(error);
    }

    await this.load();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canPost(): boolean {
    return this.auth.can('Inventory.Transfer.Post');
  }

  protected nameOf(item: Item): string {
    const description =
      this.i18n.language() === 'ar' && item.descriptionArabic
        ? item.descriptionArabic
        : item.description;

    return `${item.no} — ${description}`;
  }

  protected describe(line: TransferLine): string {
    return this.i18n.language() === 'ar' && line.descriptionArabic
      ? line.descriptionArabic
      : line.description;
  }

  protected quantity(value: number): string {
    return new Intl.NumberFormat(this.i18n.locale(), { maximumFractionDigits: 5 }).format(value);
  }

  /** How much of a transfer is between the two locations right now. */
  protected inTransitTotal(transfer: Transfer): number {
    return transfer.lines.reduce((total, line) => total + line.inTransit, 0);
  }

  /** Shipping is only possible while something on the transfer has not left. */
  protected canShip(transfer: Transfer): boolean {
    return (
      this.canPost() &&
      transfer.lines.some((line) => line.quantity - line.quantityShipped > 0) &&
      transfer.status !== 'Cancelled'
    );
  }

  /** Receiving is only possible once something is actually in transit. */
  protected canReceive(transfer: Transfer): boolean {
    return this.canPost() && transfer.lines.some((line) => line.inTransit > 0);
  }

  protected toggle(transferNo: string): void {
    this.expanded.update((current) => (current === transferNo ? null : transferNo));
  }

  protected arrivalOf(transfer: Transfer, line: TransferLine): number | null {
    return this.arrivals.get(`${transfer.no}/${line.itemNo}`) ?? null;
  }

  protected setArrival(transfer: Transfer, line: TransferLine, value: number | null): void {
    const key = `${transfer.no}/${line.itemNo}`;

    if (value === null || value === line.inTransit) {
      this.arrivals.delete(key);
      return;
    }

    this.arrivals.set(key, value);
  }

  protected addLine(): void {
    this.draft = [...this.draft, { itemNo: '', quantity: null }];
  }

  protected removeLine(index: number): void {
    this.draft = this.draft.filter((_, position) => position !== index);
  }

  protected canCreate(): boolean {
    return (
      !!this.fromLocationCode &&
      !!this.toLocationCode &&
      this.draft.some((line) => line.itemNo && (line.quantity ?? 0) > 0)
    );
  }

  protected async create(): Promise<void> {
    if (!this.canCreate() || this.busy()) {
      return;
    }

    this.messages.clear();
    this.busy.set('create');

    try {
      const result = await this.inventory.createTransfer({
        fromLocationCode: this.fromLocationCode,
        toLocationCode: this.toLocationCode,
        lines: this.draft
          .filter((line) => line.itemNo && (line.quantity ?? 0) > 0)
          .map((line) => ({ itemNo: line.itemNo, quantity: line.quantity as number })),
        description: this.description || undefined,
      });

      this.messages.showAll(result.messages ?? []);
      this.messages.showSuccess(this.t('inventory.transfers.created', { No: result.transfer.no }));

      this.draft = [{ itemNo: '', quantity: null }];
      this.description = '';

      await this.load();
    } catch (error) {
      this.messages.showError(error, this.t('inventory.transfers.create'));
    } finally {
      this.busy.set(null);
    }
  }

  protected async ship(transfer: Transfer): Promise<void> {
    await this.move(transfer, 'ship', () => this.inventory.shipTransfer(transfer.no), 'inventory.transfers.shippedAs');
  }

  protected async receive(transfer: Transfer): Promise<void> {
    const shortages: Record<string, number> = {};

    for (const line of transfer.lines) {
      const arrival = this.arrivalOf(transfer, line);

      if (arrival !== null) {
        shortages[line.itemNo] = arrival;
      }
    }

    await this.move(
      transfer,
      'receive',
      () => this.inventory.receiveTransfer(transfer.no, Object.keys(shortages).length ? shortages : undefined),
      'inventory.transfers.receivedAs',
    );
  }

  /** Shipping and receiving differ only in which call is made and what is said afterwards. */
  private async move(
    transfer: Transfer,
    action: string,
    call: () => Promise<TransferMoveReceipt>,
    successKey: TranslationKey,
  ): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.messages.clear();
    this.busy.set(`${action}:${transfer.no}`);

    try {
      const receipt = await call();

      // Warnings ride back with the success -- a short receipt, or stock that went below zero at
      // the source. They are the whole reason this is not a silent operation.
      this.messages.showAll(receipt.messages ?? []);
      this.messages.showSuccess(
        this.t(successKey, { No: transfer.no, Transaction: receipt.transactionNo }),
      );

      for (const line of transfer.lines) {
        this.arrivals.delete(`${transfer.no}/${line.itemNo}`);
      }

      await this.load();
    } catch (error) {
      this.messages.showError(error, this.t(`inventory.transfers.${action}` as TranslationKey));
    } finally {
      this.busy.set(null);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      this.transfers.set(await this.inventory.transfers());
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

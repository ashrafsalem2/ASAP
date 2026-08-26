import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Item, StockLocation, StockMovement } from '../../core/api/asap-api.models';
import { InventoryService } from '../../core/api/inventory.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

@Component({
  selector: 'asap-stock-movements',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './stock-movements.html',
  styleUrl: './stock-movements.scss',
})
export class StockMovements implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly inventory = inject(InventoryService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly movements = signal<StockMovement[]>([]);
  protected readonly items = signal<Item[]>([]);
  protected readonly locations = signal<StockLocation[]>([]);
  protected readonly loading = signal(true);
  protected readonly posting = signal(false);

  protected filterItemNo = '';

  // The movement being prepared. Purchase and Sale are the two anyone reaches for; the rest are
  // there because a stock count or a write-off has to be recordable without inventing a document.
  protected readonly entryTypes = [
    'Purchase',
    'Sale',
    'PositiveAdjustment',
    'NegativeAdjustment',
  ] as const;

  protected itemNo = '';
  protected locationCode = '';
  protected entryType: string = 'Purchase';
  protected quantity: number | null = null;
  protected unitCost: number | null = null;
  protected documentNo = '';

  async ngOnInit(): Promise<void> {
    try {
      const [items, locations] = await Promise.all([
        this.inventory.items(),
        this.inventory.locations(),
      ]);

      this.items.set(items);

      // Blocked locations are left out rather than shown and refused. Offering a choice that will
      // be rejected is a way of wasting somebody's time twice.
      this.locations.set(locations.filter((location) => !location.isBlocked));
    } catch (error) {
      this.messages.showError(error);
    }

    await this.load();
  }

  protected t(key: TranslationKey): string {
    return this.i18n.translate(key);
  }

  protected canPost(): boolean {
    return this.auth.can('Inventory.Stock.Post');
  }

  protected nameOf(item: Item): string {
    const description =
      this.i18n.language() === 'ar' && item.descriptionArabic
        ? item.descriptionArabic
        : item.description;

    return `${item.no} — ${description}`;
  }

  protected quantityOf(value: number): string {
    return new Intl.NumberFormat(this.i18n.locale(), { maximumFractionDigits: 5 }).format(value);
  }

  /** A receipt needs a cost; an issue works its own out from what is on hand. */
  protected needsUnitCost(): boolean {
    return this.entryType === 'Purchase' || this.entryType === 'PositiveAdjustment';
  }

  protected async load(): Promise<void> {
    this.loading.set(true);

    try {
      this.movements.set(await this.inventory.movements(this.filterItemNo || undefined));
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected async post(): Promise<void> {
    if (this.posting() || !this.itemNo || !this.locationCode || !this.quantity) {
      return;
    }

    this.messages.clear();
    this.posting.set(true);

    try {
      const receipt = await this.inventory.post({
        movements: [
          {
            itemNo: this.itemNo,
            locationCode: this.locationCode,

            // The sign is decided by the movement, not typed. Asking somebody to remember that a
            // sale is a negative number is asking for the mistake that puts stock up when it
            // should go down.
            quantity: this.isInbound() ? Math.abs(this.quantity) : -Math.abs(this.quantity),
            unitCost: this.needsUnitCost() ? (this.unitCost ?? 0) : 0,
            entryType: this.entryType,
          },
        ],
        documentNo: this.documentNo || undefined,
        sourceCode: 'INVJNL',
      });

      // Warnings ride back with the success. A movement that took stock below zero has to say so
      // on the screen, not only in the log. Passed through as the server rendered them: the
      // client used to rebuild each one field by field, which is how the resolution and the
      // override flag went missing without anything failing.
      this.messages.showAll(receipt.messages ?? []);

      this.messages.showSuccess(
        this.i18n.translate('inventory.movements.posted', { No: receipt.transactionNo }),
      );

      this.reset();
      await this.load();
    } catch (error) {
      this.messages.showError(error, this.t('inventory.movements.post'));
    } finally {
      this.posting.set(false);
    }
  }

  private isInbound(): boolean {
    return this.entryType === 'Purchase' || this.entryType === 'PositiveAdjustment';
  }

  private reset(): void {
    this.quantity = null;
    this.unitCost = null;
    this.documentNo = '';
  }
}

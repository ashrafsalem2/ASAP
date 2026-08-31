import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { StockAvailabilityRow, StockReservationRow } from '../../core/api/asap-api.models';
import { AuthService } from '../../core/auth/auth.service';
import { InventoryService } from '../../core/api/inventory.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * What stock is promised, and to what.
 *
 * The two tables answer different questions and both are needed. The first says what is left to
 * promise, which is the only figure anybody can act on. The second says who is holding the rest —
 * and it is the only place a hold against an abandoned order ever shows up, because a reservation
 * posts nothing and no report will ever look wrong because of one.
 */
@Component({
  selector: 'asap-stock-reservations',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './reservations.html',
})
export class Reservations implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(InventoryService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly availability = signal<StockAvailabilityRow[]>([]);
  protected readonly held = signal<StockReservationRow[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);

  protected itemFilter = '';
  protected showSpent = false;
  protected releaseReason = '';

  protected reserveItemNo = '';
  protected reserveLocationCode = '';
  protected reserveQuantity: number | null = null;
  protected reserveDocumentNo = '';

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
    return this.auth.can('Inventory.Reservation.Update');
  }

  protected async refresh(): Promise<void> {
    const [availability, held] = await Promise.all([
      this.api.stockAvailable(this.itemFilter || undefined),
      this.api.reservations(undefined, !this.showSpent),
    ]);

    this.availability.set(availability);
    this.held.set(held);
  }

  protected async reserve(): Promise<void> {
    if (this.busy() || !this.reserveItemNo.trim() || !this.reserveDocumentNo.trim()) {
      return;
    }

    this.busy.set('reserve');
    this.messages.clear();

    try {
      const result = await this.api.reserveStock({
        itemNo: this.reserveItemNo.trim(),
        locationCode: this.reserveLocationCode.trim(),
        quantity: this.reserveQuantity ?? 0,
        documentNo: this.reserveDocumentNo.trim(),
        sourceCode: 'MANUAL',
      });

      this.messages.showSuccess(
        this.t('inventory.reservations.reserved.done', {
          Quantity: result.quantity,
          ItemNo: result.itemNo,
          DocumentNo: result.documentNo,
        }),
      );

      this.reserveQuantity = null;
      await this.refresh();
    } catch (error) {
      this.messages.showError(error, this.t('inventory.reservations.reserve'));
    } finally {
      this.busy.set(null);
    }
  }

  protected async release(row: StockReservationRow): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.busy.set(row.documentNo);
    this.messages.clear();

    try {
      const result = await this.api.releaseStock(
        row.documentNo,
        this.releaseReason.trim() || undefined,
      );

      this.messages.showSuccess(
        this.t('inventory.reservations.released', { Quantity: result.released }),
      );

      await this.refresh();
    } catch (error) {
      this.messages.showError(error, this.t('inventory.reservations.release'));
    } finally {
      this.busy.set(null);
    }
  }

  /**
   * Whether more is promised than exists.
   *
   * Only possible when goods went out from under a hold -- somebody overrode the block, or the
   * stock was adjusted away. Worth flagging rather than hiding: it is a promise that cannot be
   * kept and nothing else in the system will say so.
   *
   * Stock that is merely negative is not this. A shelf below zero is a different problem with its
   * own flag on the stock-on-hand screen, and nobody has been promised anything.
   */
  protected isOverPromised(row: StockAvailabilityRow): boolean {
    return row.quantityReserved > 0 && row.quantityReserved > row.quantityOnHand;
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }
}

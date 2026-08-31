import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  BinContent,
  BinMovementRow,
  Item,
  StockLocation,
} from '../../core/api/asap-api.models';
import { AuthService } from '../../core/auth/auth.service';
import { InventoryService } from '../../core/api/inventory.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/** A line being prepared, before it is sent. */
interface DraftLine {
  itemNo: string;
  fromBinCode: string;
  toBinCode: string;
  quantity: number | null;
}

/**
 * Goods moved between shelves inside one place.
 *
 * The sheet is built up and posted as one act, because that is how somebody restocking works —
 * eleven things at once — and because posting ten of eleven leaves the shelf and the record
 * disagreeing with nothing to say which ten went through.
 *
 * What is on each shelf is shown beside the draft rather than left to be remembered. The commonest
 * refusal by far is moving more than is there, and it is the one that is cheapest to avoid.
 */
@Component({
  selector: 'asap-bin-movements',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './bin-movements.html',
})
export class BinMovements implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(InventoryService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly movements = signal<BinMovementRow[]>([]);
  protected readonly locations = signal<StockLocation[]>([]);
  protected readonly items = signal<Item[]>([]);
  protected readonly contents = signal<BinContent[]>([]);
  protected readonly draft = signal<DraftLine[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);

  protected locationCode = '';
  protected note = '';

  protected newItemNo = '';
  protected newFromBin = '';
  protected newToBin = '';
  protected newQuantity: number | null = null;

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canWrite(): boolean {
    return this.auth.can('Inventory.Item.Update');
  }

  /** Only bin-tracked places can have a movement at all. */
  protected binned(): StockLocation[] {
    return this.locations().filter((location) => location.usesBins);
  }

  /** The shelves holding the chosen item, so the source is picked from what exists. */
  protected shelvesHolding(itemNo: string): BinContent[] {
    return this.contents().filter((row) => row.itemNo === itemNo && row.quantity > 0);
  }

  /** Every shelf that has been seen at this location, for the destination. */
  protected allShelves(): string[] {
    return [...new Set(this.contents().map((row) => row.binCode))].sort();
  }

  protected heldIn(itemNo: string, binCode: string): number {
    return (
      this.contents().find((row) => row.itemNo === itemNo && row.binCode === binCode)?.quantity ?? 0
    );
  }

  protected addLine(): void {
    if (!this.newItemNo || !this.newFromBin || !this.newToBin || !this.newQuantity) {
      return;
    }

    this.draft.update((lines) => [
      ...lines,
      {
        itemNo: this.newItemNo,
        fromBinCode: this.newFromBin,
        toBinCode: this.newToBin,
        quantity: this.newQuantity,
      },
    ]);

    this.newQuantity = null;
  }

  protected removeLine(index: number): void {
    this.draft.update((lines) => lines.filter((_, i) => i !== index));
  }

  protected async chooseLocation(): Promise<void> {
    this.draft.set([]);
    this.newFromBin = '';
    this.newToBin = '';

    await this.loadContents();
    await this.loadMovements();
  }

  protected async post(): Promise<void> {
    if (!this.locationCode || this.draft().length === 0) {
      return;
    }

    this.busy.set(true);

    try {
      const result = await this.api.postBinMovement({
        locationCode: this.locationCode,
        lines: this.draft().map((line) => ({
          itemNo: line.itemNo,
          fromBinCode: line.fromBinCode,
          toBinCode: line.toBinCode,
          quantity: line.quantity ?? 0,
        })),
        note: this.note || null,
      });

      this.messages.showSuccess(
        this.t('inventory.binMovements.posted', {
          No: result.movement.no,
          Count: result.movement.lines.length,
        }),
      );

      this.draft.set([]);
      this.note = '';

      await this.loadContents();
      await this.loadMovements();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  private async loadContents(): Promise<void> {
    if (!this.locationCode) {
      this.contents.set([]);

      return;
    }

    try {
      this.contents.set(await this.api.binContents(this.locationCode));
    } catch (error) {
      this.messages.showError(error);
    }
  }

  private async loadMovements(): Promise<void> {
    try {
      this.movements.set(await this.api.binMovements(this.locationCode || undefined));
    } catch (error) {
      this.messages.showError(error);
    }
  }

  private async reload(): Promise<void> {
    this.loading.set(true);

    try {
      const [locations, items] = await Promise.all([this.api.locations(), this.api.items()]);

      this.locations.set(locations);
      this.items.set(items.filter((item) => !item.isBlocked));

      // Straight to the only bin-tracked place, where there is only one. Most shops have one
      // warehouse, and making them pick it every time is a click that answers nothing.
      const only = this.binned();

      if (only.length === 1) {
        this.locationCode = only[0].code;
        await this.loadContents();
      }

      await this.loadMovements();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

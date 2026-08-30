import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Item, ItemUnit, ResolvedQuantity, UnitOfMeasure } from '../../core/api/asap-api.models';
import { InventoryService } from '../../core/api/inventory.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * What the company measures in, and what one item's box holds.
 *
 * Two halves of one setup on one screen, because they are only useful together: the unit list says
 * the word `CASE` exists, and the item says what a case of this particular thing holds. One
 * without the other answers nothing.
 *
 * The scan box at the bottom is the part a shop actually uses. Somebody holds a barcode against
 * the reader and the screen says which item it is and how many it stands for, which is the one
 * question a duplicate barcode or a missing conversion makes unanswerable.
 */
@Component({
  selector: 'asap-units',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './units.html',
})
export class Units implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(InventoryService);
  private readonly messages = inject(MessageService);

  protected readonly units = signal<UnitOfMeasure[]>([]);
  protected readonly items = signal<Item[]>([]);
  protected readonly itemUnits = signal<ItemUnit[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);

  protected readonly selectedItem = signal('');

  /** A new unit for the company list. */
  protected newCode = '';
  protected newName = '';
  protected newNameArabic = '';
  protected newPlaces = 0;

  /** A new conversion on the chosen item. */
  protected addUnitCode = '';
  protected addPerUnit: number | null = null;
  protected addBarcode = '';

  /** The scan box. */
  protected barcode = '';
  protected readonly scanned = signal<ResolvedQuantity | null>(null);

  async ngOnInit(): Promise<void> {
    try {
      const [units, items] = await Promise.all([this.api.units(true), this.api.items()]);

      this.units.set(units);
      this.items.set(items);

      if (items.length > 0) {
        await this.chooseItem(items[0].no);
      }
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected async chooseItem(itemNo: string): Promise<void> {
    this.selectedItem.set(itemNo);
    this.addUnitCode = '';
    this.addPerUnit = null;
    this.addBarcode = '';

    if (!itemNo) {
      this.itemUnits.set([]);
      return;
    }

    try {
      this.itemUnits.set(await this.api.itemUnits(itemNo));
    } catch (error) {
      this.itemUnits.set([]);
      this.messages.showError(error);
    }
  }

  protected async onItemChange(event: Event): Promise<void> {
    await this.chooseItem((event.target as HTMLSelectElement).value);
  }

  protected async addUnit(): Promise<void> {
    if (!this.newCode.trim()) {
      return;
    }

    this.busy.set(true);

    try {
      await this.api.saveUnit({
        code: this.newCode.trim().toUpperCase(),
        name: this.newName.trim() || this.newCode.trim().toUpperCase(),
        nameArabic: this.newNameArabic.trim() || undefined,
        decimalPlaces: Number(this.newPlaces) || 0,
        isActive: true,
      });

      this.units.set(await this.api.units(true));

      this.newCode = '';
      this.newName = '';
      this.newNameArabic = '';
      this.newPlaces = 0;
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async addConversion(): Promise<void> {
    const itemNo = this.selectedItem();

    if (!itemNo || !this.addUnitCode || this.addPerUnit === null) {
      return;
    }

    this.busy.set(true);

    try {
      await this.api.saveItemUnit(itemNo, {
        unitCode: this.addUnitCode,
        quantityPerUnit: Number(this.addPerUnit),
        barcode: this.addBarcode.trim() || null,
      });

      await this.chooseItem(itemNo);
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(unitCode: string): Promise<void> {
    const itemNo = this.selectedItem();

    this.busy.set(true);

    try {
      await this.api.removeItemUnit(itemNo, unitCode);
      await this.chooseItem(itemNo);
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async scan(): Promise<void> {
    const code = this.barcode.trim();

    if (!code) {
      return;
    }

    this.scanned.set(null);

    try {
      this.scanned.set(await this.api.scan(code));
    } catch (error) {
      this.messages.showError(error);
    }
  }

  /** The units this item does not already have, so the same one is not offered twice. */
  protected available(): UnitOfMeasure[] {
    const taken = new Set(this.itemUnits().map((u) => u.unitCode.toUpperCase()));

    return this.units().filter((u) => u.isActive && !taken.has(u.code.toUpperCase()));
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected unitName(unit: UnitOfMeasure): string {
    return this.i18n.language() === 'ar' && unit.nameArabic ? unit.nameArabic : unit.name;
  }

  /** What a scan came back as, in the language the reader is using. */
  protected scannedName(hit: ResolvedQuantity): string {
    return this.i18n.language() === 'ar' && hit.descriptionArabic
      ? hit.descriptionArabic
      : hit.description;
  }

  protected itemName(item: Item): string {
    return this.i18n.language() === 'ar' && item.descriptionArabic
      ? item.descriptionArabic
      : item.description;
  }
}

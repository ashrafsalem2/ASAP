import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Item, ItemVariant, VariantStockRow } from '../../core/api/asap-api.models';
import { InventoryService } from '../../core/api/inventory.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * The colours, sizes and flavours an item is stocked as.
 *
 * A variant is not a bin. A bin says where the same goods are standing and never touches a cost; a
 * variant is a different physical thing that may have cost a different amount. So stock, cost
 * layers and valuation all split again per variant — which is why the stock table below is per
 * variant and there is no total across them. "How many shirts" is not a question a shop asks; "have
 * we got that one in medium" is.
 */
@Component({
  selector: 'asap-variants',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './variants.html',
})
export class Variants implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(InventoryService);
  private readonly messages = inject(MessageService);

  protected readonly items = signal<Item[]>([]);
  protected readonly variants = signal<ItemVariant[]>([]);
  protected readonly stock = signal<VariantStockRow[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);

  protected readonly selected = signal<Item | null>(null);

  /** A new variant. */
  protected code = '';
  protected description = '';
  protected descriptionArabic = '';
  protected barcode = '';
  protected sortOrder = 0;

  async ngOnInit(): Promise<void> {
    try {
      const items = await this.api.items();

      this.items.set(items);

      if (items.length > 0) {
        await this.choose(items[0].no);
      }
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected async choose(itemNo: string): Promise<void> {
    const item = this.items().find((i) => i.no === itemNo) ?? null;

    this.selected.set(item);
    this.code = '';
    this.description = '';
    this.descriptionArabic = '';
    this.barcode = '';
    this.sortOrder = 0;

    if (!item) {
      this.variants.set([]);
      this.stock.set([]);
      return;
    }

    await this.refresh(item.no);
  }

  protected async onItemChange(event: Event): Promise<void> {
    await this.choose((event.target as HTMLSelectElement).value);
  }

  protected async add(): Promise<void> {
    const item = this.selected();

    if (!item || !this.code.trim()) {
      return;
    }

    this.busy.set(true);

    try {
      await this.api.saveItemVariant(item.no, {
        code: this.code.trim().toUpperCase(),
        description: this.description.trim() || this.code.trim().toUpperCase(),
        descriptionArabic: this.descriptionArabic.trim() || null,
        barcode: this.barcode.trim() || null,
        sortOrder: Number(this.sortOrder) || 0,
        isBlocked: false,
      });

      this.code = '';
      this.description = '';
      this.descriptionArabic = '';
      this.barcode = '';
      this.sortOrder = 0;

      await this.refresh(item.no);
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async setBlocked(variant: ItemVariant, blocked: boolean): Promise<void> {
    const item = this.selected();

    if (!item) {
      return;
    }

    this.busy.set(true);

    try {
      await this.api.saveItemVariant(item.no, { ...variant, isBlocked: blocked });
      await this.refresh(item.no);
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async turnOff(): Promise<void> {
    const item = this.selected();

    if (!item) {
      return;
    }

    this.busy.set(true);

    try {
      await this.api.setItemHasVariants(item.no, false);

      this.items.set(await this.api.items());
      await this.choose(item.no);
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  /** What one variant is holding, across every location. */
  protected held(code: string): number {
    return this.stock()
      .filter((row) => row.variantCode === code)
      .reduce((total, row) => total + row.quantity, 0);
  }

  protected variantName(variant: ItemVariant): string {
    return this.i18n.language() === 'ar' && variant.descriptionArabic
      ? variant.descriptionArabic
      : variant.description;
  }

  protected itemName(item: Item): string {
    return this.i18n.language() === 'ar' && item.descriptionArabic
      ? item.descriptionArabic
      : item.description;
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  private async refresh(itemNo: string): Promise<void> {
    try {
      const [variants, stock] = await Promise.all([
        this.api.itemVariants(itemNo),
        this.api.variantStock(itemNo),
      ]);

      this.variants.set(variants);
      this.stock.set(stock);
    } catch (error) {
      this.variants.set([]);
      this.stock.set([]);
      this.messages.showError(error);
    }
  }
}

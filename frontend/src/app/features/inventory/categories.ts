import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CategoryPostingGap, Item, ItemCategory } from '../../core/api/asap-api.models';
import { InventoryService } from '../../core/api/inventory.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * How items are grouped, and which accounts each group posts to.
 *
 * Accounts live on the category rather than the item, so a company with twelve thousand items
 * maintains six sets of accounts rather than twelve thousand. That is the reason the grouping
 * exists at all.
 *
 * Which makes the panel at the top the important one. A movement under a category with no
 * inventory account posts no ledger lines, on purpose — refusing it would stop a shop trading over
 * a setup step nobody has reached. The cost is that a company can run for months with its inventory
 * account frozen and nothing ever says so. This is the thing that says so, and it shows what has
 * already gone unposted rather than only what will.
 */
@Component({
  selector: 'asap-categories',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './categories.html',
})
export class Categories implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(InventoryService);
  private readonly messages = inject(MessageService);

  protected readonly categories = signal<ItemCategory[]>([]);
  protected readonly gaps = signal<CategoryPostingGap[]>([]);
  protected readonly items = signal<Item[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);

  /** The category being edited, or a blank one being added. */
  protected code = '';
  protected name = '';
  protected nameArabic = '';
  protected parentCode = '';
  protected inventoryAccountNo = '';
  protected costOfGoodsSoldAccountNo = '';
  protected salesAccountNo = '';
  protected varianceAccountNo = '';

  /** Moving one item. */
  protected itemNo = '';
  protected itemCategoryCode = '';

  async ngOnInit(): Promise<void> {
    try {
      await this.refresh();
      this.items.set(await this.api.items());
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected edit(category: ItemCategory): void {
    this.code = category.code;
    this.name = category.name;
    this.nameArabic = category.nameArabic ?? '';
    this.parentCode = category.parentCode ?? '';
    this.inventoryAccountNo = category.inventoryAccountNo ?? '';
    this.costOfGoodsSoldAccountNo = category.costOfGoodsSoldAccountNo ?? '';
    this.salesAccountNo = category.salesAccountNo ?? '';
    this.varianceAccountNo = category.varianceAccountNo ?? '';
  }

  protected clear(): void {
    this.code = '';
    this.name = '';
    this.nameArabic = '';
    this.parentCode = '';
    this.inventoryAccountNo = '';
    this.costOfGoodsSoldAccountNo = '';
    this.salesAccountNo = '';
    this.varianceAccountNo = '';
  }

  protected async save(): Promise<void> {
    if (!this.code.trim()) {
      return;
    }

    this.busy.set(true);

    try {
      await this.api.saveItemCategory({
        code: this.code.trim().toUpperCase(),
        name: this.name.trim() || this.code.trim().toUpperCase(),
        nameArabic: this.nameArabic.trim() || null,
        parentCode: this.parentCode.trim() || null,
        inventoryAccountNo: this.inventoryAccountNo.trim() || null,
        costOfGoodsSoldAccountNo: this.costOfGoodsSoldAccountNo.trim() || null,
        salesAccountNo: this.salesAccountNo.trim() || null,
        varianceAccountNo: this.varianceAccountNo.trim() || null,
        itemCount: 0,
      });

      await this.refresh();
      this.clear();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async moveItem(): Promise<void> {
    if (!this.itemNo) {
      return;
    }

    this.busy.set(true);

    try {
      await this.api.setItemCategory(this.itemNo, this.itemCategoryCode || null);
      await this.refresh();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  /** What has gone unposted across every gap, added up. */
  protected unpostedTotal(): number {
    return this.gaps().reduce((sum, gap) => sum + gap.unpostedValue, 0);
  }

  protected categoryName(category: ItemCategory): string {
    return this.i18n.language() === 'ar' && category.nameArabic ? category.nameArabic : category.name;
  }

  protected gapName(gap: CategoryPostingGap): string {
    return this.i18n.language() === 'ar' && gap.nameArabic ? gap.nameArabic : gap.name;
  }

  /** The missing accounts, named the way the screen labels them. */
  protected missingLabels(gap: CategoryPostingGap): string {
    const labels: Record<string, TranslationKey> = {
      InventoryAccountNo: 'inventory.categories.inventoryAccount',
      CostOfGoodsSoldAccountNo: 'inventory.categories.cogsAccount',
      SalesAccountNo: 'inventory.categories.salesAccount',
      VarianceAccountNo: 'inventory.categories.varianceAccount',
    };

    return gap.missingAccounts.map((field) => this.t(labels[field] ?? 'common.loading')).join(', ');
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  private async refresh(): Promise<void> {
    const [categories, gaps] = await Promise.all([
      this.api.itemCategories(),
      this.api.categoryPostingGaps(),
    ]);

    this.categories.set(categories);
    this.gaps.set(gaps);
  }
}

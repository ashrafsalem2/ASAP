import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CustomerPriceList, PriceList } from '../../core/api/asap-api.models';
import { SalesService } from '../../core/api/sales.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * What each customer pays, rather than what the item says.
 *
 * A sheet is edited and saved whole, because that is how a contract is agreed: the prices here
 * replace the prices held rather than merging into them. A half-applied sheet is a set of prices
 * nobody agreed to.
 *
 * The rule worth understanding is which line wins when several fit. The most specific one does — a
 * price for one colour beats a price for the item, and a price from a hundred up beats a price for
 * any quantity. Two lines equally specific are refused rather than resolved, and the refusal comes
 * from the server rather than from here, so it applies to an order taken by any route.
 */
@Component({
  selector: 'asap-price-lists',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './price-lists.html',
})
export class PriceLists implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(SalesService);
  private readonly messages = inject(MessageService);

  protected readonly lists = signal<PriceList[]>([]);
  protected readonly assignments = signal<CustomerPriceList[]>([]);
  protected readonly editing = signal<PriceList | null>(null);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);

  protected assignCustomerNo = '';
  protected assignListCode = '';

  async ngOnInit(): Promise<void> {
    try {
      await this.refresh();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected async refresh(): Promise<void> {
    const [lists, assignments] = await Promise.all([
      this.api.priceLists(),
      this.api.priceListAssignments(),
    ]);

    this.lists.set(lists);
    this.assignments.set(assignments);
  }

  protected edit(list: PriceList): void {
    // A copy, so abandoning an edit leaves the table showing what is actually saved.
    this.editing.set({ ...list, lines: list.lines.map((line) => ({ ...line })) });
  }

  protected startNew(): void {
    this.editing.set({
      code: '',
      name: '',
      nameArabic: null,
      validFrom: null,
      validTo: null,
      isActive: true,
      lines: [],
    });
  }

  protected cancel(): void {
    this.editing.set(null);
  }

  protected addLine(): void {
    const list = this.editing();

    if (!list) {
      return;
    }

    this.editing.set({
      ...list,
      lines: [
        ...list.lines,
        {
          itemNo: '',
          variantCode: null,
          unitCode: null,
          minimumQuantity: 0,
          unitPrice: 0,
          discountPercent: 0,
          validFrom: null,
          validTo: null,
        },
      ],
    });
  }

  protected removeLine(index: number): void {
    const list = this.editing();

    if (!list) {
      return;
    }

    this.editing.set({ ...list, lines: list.lines.filter((_, at) => at !== index) });
  }

  protected async save(): Promise<void> {
    const list = this.editing();

    if (!list || !list.code.trim()) {
      return;
    }

    this.saving.set(true);

    try {
      await this.api.savePriceList({
        ...list,
        lines: list.lines.filter((line) => line.itemNo.trim().length > 0),
      });

      this.messages.showSuccess(this.t('sales.priceLists.saved'));
      this.editing.set(null);
      await this.refresh();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.saving.set(false);
    }
  }

  protected async assign(): Promise<void> {
    if (!this.assignCustomerNo.trim()) {
      return;
    }

    try {
      await this.api.assignPriceList(this.assignCustomerNo, this.assignListCode || null);

      this.messages.showSuccess(this.t('sales.priceLists.assigned'));
      this.assignCustomerNo = '';
      this.assignListCode = '';
      await this.refresh();
    } catch (error) {
      this.messages.showError(error);
    }
  }

  protected trackLine(index: number): number {
    return index;
  }

  protected t(key: TranslationKey): string {
    return this.i18n.translate(key);
  }
}

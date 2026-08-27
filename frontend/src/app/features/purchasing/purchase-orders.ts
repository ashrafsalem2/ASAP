import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import {
  Item,
  Party,
  PurchaseOrder,
  StockLocation,
  TaxCodeSummary,
} from '../../core/api/asap-api.models';
import { FinanceService } from '../../core/api/finance.service';
import { InventoryService } from '../../core/api/inventory.service';
import { PurchasingService } from '../../core/api/purchasing.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/** One line being drafted onto a new order. */
interface DraftLine {
  type: 'Item' | 'GlAccount';
  no: string;
  quantity: number | null;
  directUnitCost: number | null;
  taxCode: string;
}

/**
 * Purchase orders, and the form that raises one.
 *
 * Raising an order posts nothing, and the screen says so: the two things that do post happen
 * afterwards, on the order's own page, where what has arrived and what has been billed can be
 * seen side by side.
 */
@Component({
  selector: 'asap-purchase-orders',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink],
  templateUrl: './purchase-orders.html',
  styleUrl: './purchasing.scss',
})
export class PurchaseOrders implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly purchasing = inject(PurchasingService);
  private readonly finance = inject(FinanceService);
  private readonly inventory = inject(InventoryService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly orders = signal<PurchaseOrder[]>([]);
  protected readonly vendors = signal<Party[]>([]);
  protected readonly items = signal<Item[]>([]);
  protected readonly locations = signal<StockLocation[]>([]);
  protected readonly accounts = signal<{ no: string; label: string }[]>([]);
  protected readonly taxCodes = signal<TaxCodeSummary[]>([]);
  protected readonly loading = signal(true);
  protected readonly creating = signal(false);

  protected vendorNo = '';
  protected locationCode = '';
  protected description = '';
  protected draft: DraftLine[] = [this.blankLine()];

  async ngOnInit(): Promise<void> {
    try {
      const [vendors, items, locations, accounts, taxCodes] = await Promise.all([
        this.finance.parties('Vendor'),
        this.inventory.items(),
        this.inventory.locations(),
        this.finance.accounts(),
        this.finance.taxCodes(),
      ]);

      // Blocked parties and locations are left out rather than offered and refused.
      this.vendors.set(vendors.filter((vendor) => !vendor.isBlocked));
      this.items.set(items);
      this.locations.set(locations.filter((location) => !location.isBlocked && !location.isInTransit));
      this.taxCodes.set(taxCodes);

      this.accounts.set(
        accounts
          .filter((account) => account.accountType === 'Posting' && !account.isBlocked)
          .map((account) => ({
            no: account.no,
            label: `${account.no} — ${this.i18n.language() === 'ar' && account.nameArabic ? account.nameArabic : account.name}`,
          })),
      );
    } catch (error) {
      this.messages.showError(error);
    }

    await this.load();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canCreate(): boolean {
    return this.auth.can('Purchasing.Order.Create');
  }

  protected nameOf(party: Party): string {
    const name = this.i18n.language() === 'ar' && party.nameArabic ? party.nameArabic : party.name;

    return `${party.no} — ${name}`;
  }

  /** What a line of this type can buy, so the second dropdown only offers valid choices. */
  protected choicesFor(line: DraftLine): { value: string; label: string }[] {
    if (line.type === 'GlAccount') {
      return this.accounts().map((account) => ({ value: account.no, label: account.label }));
    }

    return this.items().map((item) => ({
      value: item.no,
      label: `${item.no} — ${this.i18n.language() === 'ar' && item.descriptionArabic ? item.descriptionArabic : item.description}`,
    }));
  }

  protected taxLabel(code: TaxCodeSummary): string {
    const name =
      this.i18n.language() === 'ar' && code.descriptionArabic
        ? code.descriptionArabic
        : code.description;

    return code.percentage > 0 ? `${code.code} — ${code.percentage}%` : `${code.code} — ${name}`;
  }

  /** Changing what a line buys clears what it was pointing at, which is no longer valid. */
  protected changeType(index: number, type: 'Item' | 'GlAccount'): void {
    this.draft = this.draft.map((line, position) =>
      position === index ? { ...line, type, no: '' } : line,
    );
  }

  protected addLine(): void {
    this.draft = [...this.draft, this.blankLine()];
  }

  protected removeLine(index: number): void {
    this.draft = this.draft.filter((_, position) => position !== index);
  }

  protected isReady(): boolean {
    return (
      !!this.vendorNo &&
      this.draft.some((line) => line.no && (line.quantity ?? 0) > 0)
    );
  }

  protected async create(): Promise<void> {
    if (!this.isReady() || this.creating()) {
      return;
    }

    this.messages.clear();
    this.creating.set(true);

    try {
      const result = await this.purchasing.create({
        vendorNo: this.vendorNo,
        locationCode: this.locationCode || undefined,
        description: this.description || undefined,
        lines: this.draft
          .filter((line) => line.no && (line.quantity ?? 0) > 0)
          .map((line) => ({
            type: line.type,
            no: line.no,
            quantity: line.quantity as number,
            directUnitCost: line.directUnitCost ?? 0,
            taxCode: line.taxCode || undefined,
          })),
      });

      this.messages.showAll(result.messages ?? []);
      this.messages.showSuccess(this.t('purchasing.orders.created', { No: result.order.no }));

      this.draft = [this.blankLine()];
      this.description = '';

      await this.load();
    } catch (error) {
      this.messages.showError(error, this.t('purchasing.orders.create'));
    } finally {
      this.creating.set(false);
    }
  }

  private blankLine(): DraftLine {
    return { type: 'Item', no: '', quantity: null, directUnitCost: null, taxCode: '' };
  }

  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      this.orders.set(await this.purchasing.orders());
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

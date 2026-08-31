import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ReplenishmentRow, StockLocation } from '../../core/api/asap-api.models';
import { AuthService } from '../../core/auth/auth.service';
import { InventoryService } from '../../core/api/inventory.service';
import { PurchasingService } from '../../core/api/purchasing.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * What needs buying, and the arithmetic that says so.
 *
 * Every figure behind a suggestion is on the line rather than behind a hover or a second screen.
 * A buyer who cannot see why the worksheet asked for forty will either order forty without
 * thinking or ignore the whole screen, and both are worse than a wider table.
 *
 * The quantity is editable. What the policy asks for is a suggestion, and a buyer who knows the
 * vendor is closing next week is right to change it — but the suggested figure stays visible
 * beside it so the change is a decision somebody made rather than a number that drifted.
 */
@Component({
  selector: 'asap-replenishment',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './replenishment.html',
  styleUrl: './purchasing.scss',
})
export class Replenishment implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(PurchasingService);
  private readonly inventory = inject(InventoryService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly rows = signal<ReplenishmentRow[]>([]);
  protected readonly locations = signal<StockLocation[]>([]);
  protected readonly taking = signal<Set<string>>(new Set());
  protected readonly ordering = signal<Map<string, number>>(new Map());
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);

  protected filterLocation = '';
  protected includeSatisfied = false;

  async ngOnInit(): Promise<void> {
    this.locations.set(await this.inventory.locations().catch(() => []));

    await this.run();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canRaise(): boolean {
    return this.auth.can('Purchasing.Requisition.Create');
  }

  protected key(row: ReplenishmentRow): string {
    return `${row.itemNo}|${row.locationCode}`;
  }

  protected isTaken(row: ReplenishmentRow): boolean {
    return this.taking().has(this.key(row));
  }

  protected quantityFor(row: ReplenishmentRow): number {
    return this.ordering().get(this.key(row)) ?? row.suggestedQuantity;
  }

  protected setQuantity(row: ReplenishmentRow, value: number | string): void {
    const quantity = Number(value);
    const next = new Map(this.ordering());

    next.set(this.key(row), Number.isFinite(quantity) ? quantity : 0);
    this.ordering.set(next);
  }

  protected toggle(row: ReplenishmentRow): void {
    const next = new Set(this.taking());
    const key = this.key(row);

    if (next.has(key)) {
      next.delete(key);
    } else {
      next.add(key);
    }

    this.taking.set(next);
  }

  protected takeAll(): void {
    this.taking.set(
      new Set(
        this.rows()
          .filter((row) => row.suggestedQuantity > 0)
          .map((row) => this.key(row)),
      ),
    );
  }

  protected takeNone(): void {
    this.taking.set(new Set());
  }

  protected takenCount(): number {
    return this.taking().size;
  }

  /** Whether the shortfall is already covered by goods on their way. */
  protected isCovered(row: ReplenishmentRow): boolean {
    return row.suggestedQuantity === 0 && row.quantityOnOrder > 0;
  }

  protected async run(): Promise<void> {
    this.loading.set(true);

    try {
      this.rows.set(
        await this.api.replenishment(this.filterLocation || undefined, this.includeSatisfied),
      );

      this.taking.set(new Set());
      this.ordering.set(new Map());
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected async raise(): Promise<void> {
    const taken = this.rows()
      .filter((row) => this.isTaken(row))
      .map((row) => ({ ...row, suggestedQuantity: this.quantityFor(row) }))
      .filter((row) => row.suggestedQuantity > 0);

    if (taken.length === 0) {
      return;
    }

    this.busy.set(true);

    try {
      const raised = await this.api.takeReplenishment(taken, this.filterLocation || undefined);

      this.messages.showSuccess(
        this.t('purchasing.replenishment.raised', {
          No: raised.no,
          Count: taken.length,
        }),
      );

      await this.run();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }
}

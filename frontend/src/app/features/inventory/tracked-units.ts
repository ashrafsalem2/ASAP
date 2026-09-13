import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Item, TrackedUnitRow } from '../../core/api/asap-api.models';
import { AuthService } from '../../core/auth/auth.service';
import { InventoryService } from '../../core/api/inventory.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * Every serial and lot on hand, and what each one cost.
 *
 * The costing panel sits here rather than on a general item form because the choice only means
 * anything for the goods this screen lists, and because it is locked the moment anything posts —
 * the one time to get it right is before the first unit arrives.
 */
@Component({
  selector: 'asap-tracked-units',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './tracked-units.html',
})
export class TrackedUnits implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(InventoryService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly units = signal<TrackedUnitRow[]>([]);
  protected readonly items = signal<Item[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);

  protected readonly totalValue = computed(() => this.units().reduce((sum, unit) => sum + unit.value, 0));

  protected filterItemNo = '';

  protected costingItemNo = '';
  protected costingMethod = 'Fifo';
  protected tracking = 'None';

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canWrite(): boolean {
    return this.auth.can('Inventory.Item.Update');
  }

  protected chooseItem(): void {
    const item = this.items().find((i) => i.no === this.costingItemNo);

    this.costingMethod = item?.costingMethod ?? 'Fifo';
    this.tracking = item?.tracking ?? 'None';
  }

  /** Tracking goes with specific costing and only with it; the form keeps the two in step. */
  protected methodChanged(): void {
    if (this.costingMethod !== 'Specific') {
      this.tracking = 'None';
    } else if (this.tracking === 'None') {
      this.tracking = 'Serial';
    }
  }

  protected async filter(): Promise<void> {
    await this.reload();
  }

  protected async saveCosting(): Promise<void> {
    if (!this.costingItemNo) {
      return;
    }

    this.busy.set(true);

    try {
      await this.api.setItemCosting(this.costingItemNo, this.costingMethod, this.tracking);
      this.messages.showSuccess(this.t('inventory.trackedUnits.costingSaved', { ItemNo: this.costingItemNo }));
      this.items.set(await this.api.items());
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  private async reload(): Promise<void> {
    this.loading.set(true);

    try {
      const [units, items] = await Promise.all([
        this.api.trackedUnits(this.filterItemNo || undefined),
        this.items().length > 0 ? Promise.resolve(this.items()) : this.api.items(),
      ]);

      this.units.set(units);
      this.items.set(items);
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

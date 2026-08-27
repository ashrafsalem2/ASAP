import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { StockCount, StockCountSummary } from '../../core/api/asap-api.models';
import { InventoryService } from '../../core/api/inventory.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * Physical stock counts.
 *
 * Everything else in this system records what somebody said happened. A count is the only thing
 * that goes and looks, which makes it the only real check on the rest — and the only place a
 * shortfall nobody ever saw becomes a number somebody can act on.
 */
@Component({
  selector: 'asap-stock-counts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './stock-counts.html',
  styleUrl: './stock-counts.scss',
})
export class StockCounts implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly inventory = inject(InventoryService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly counts = signal<StockCountSummary[]>([]);
  protected readonly sheet = signal<StockCount | null>(null);
  protected readonly locations = signal<string[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);

  /** Only the lines that differ, when somebody wants to see just those. */
  protected readonly onlyDifferences = signal(false);

  protected newLocation = '';
  protected newDescription = '';
  protected overrideReason = '';

  protected readonly lines = computed(() => {
    const all = this.sheet()?.lines ?? [];

    return this.onlyDifferences() ? all.filter((l) => l.difference !== 0) : all;
  });

  /** What the count found, in units, ignoring which way each line went. */
  protected readonly counted = computed(
    () => this.sheet()?.lines.filter((l) => l.countedQuantity !== null).length ?? 0,
  );

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canCount(): boolean {
    return this.auth.can('Inventory.Count.Create');
  }

  protected canPost(): boolean {
    return this.auth.can('Inventory.Count.Post');
  }

  protected statusLabel(status: string): string {
    return this.t(`inventory.count.status.${status}` as TranslationKey);
  }

  protected async open(count: StockCountSummary): Promise<void> {
    try {
      this.sheet.set(await this.inventory.stockCount(count.no));
      this.overrideReason = '';
    } catch (error) {
      this.messages.showError(error);
    }
  }

  protected async start(): Promise<void> {
    if (!this.newLocation) {
      return;
    }

    this.busy.set('start');

    try {
      const started = await this.inventory.startStockCount({
        locationCode: this.newLocation,
        description: this.newDescription.trim() || null,
      });

      this.messages.showSuccess(
        this.t('inventory.count.started', { no: started.no, lines: started.lines.length }),
      );

      this.newDescription = '';
      this.sheet.set(started);
      await this.reload();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(null);
    }
  }

  /**
   * Records what was found on one shelf.
   *
   * An empty box clears the count rather than reading as nought. Nought is a shelf somebody
   * looked at and found empty; cleared is a shelf nobody has reached, and posting treats the two
   * very differently.
   */
  protected async record(itemNo: string, value: string): Promise<void> {
    const count = this.sheet();

    if (!count) {
      return;
    }

    const trimmed = value.trim();
    const quantity = trimmed === '' ? null : Number(trimmed);

    if (quantity !== null && Number.isNaN(quantity)) {
      return;
    }

    this.busy.set(itemNo);

    try {
      this.sheet.set(await this.inventory.recordStockCount(count.no, itemNo, quantity));
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(null);
    }
  }

  protected async post(): Promise<void> {
    const count = this.sheet();

    if (!count) {
      return;
    }

    this.busy.set('post');

    try {
      const posted = await this.inventory.postStockCount(
        count.no,
        this.overrideReason.trim() || undefined,
      );

      this.messages.showAll(posted.messages);
      this.messages.showSuccess(this.t('inventory.count.posted', { no: count.no }));

      this.overrideReason = '';
      this.sheet.set(posted.count);
      await this.reload();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(null);
    }
  }

  protected async cancel(): Promise<void> {
    const count = this.sheet();

    if (!count || !confirm(this.t('inventory.count.abandonConfirm', { no: count.no }))) {
      return;
    }

    this.busy.set('cancel');

    try {
      this.sheet.set(await this.inventory.cancelStockCount(count.no));
      await this.reload();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(null);
    }
  }

  private async reload(): Promise<void> {
    this.loading.set(true);

    try {
      const [counts, locations] = await Promise.all([
        this.inventory.stockCounts(),
        this.inventory.locations(),
      ]);

      this.counts.set(counts);
      this.locations.set(locations.map((l) => l.code));

      if (!this.newLocation && locations.length > 0) {
        this.newLocation = locations[0].code;
      }

      const current = this.sheet();

      if (current) {
        this.sheet.set(await this.inventory.stockCount(current.no));
      }
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AgeingRow, ValuationRow, VelocityRow } from '../../core/api/asap-api.models';
import { InventoryService } from '../../core/api/inventory.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * What the stock is worth, how old it is, and how fast it moves.
 *
 * The three answer different questions and share one source: the cost ledger. The running figure
 * on the item is a convenience kept up to date for the next posting; only the ledger can be asked
 * what something was worth on a date that has already gone by, which is the only kind of question
 * a period end ever asks.
 */
@Component({
  selector: 'asap-stock-analysis',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './stock-analysis.html',
})
export class StockAnalysis implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(InventoryService);
  private readonly messages = inject(MessageService);

  protected readonly valuation = signal<ValuationRow[]>([]);
  protected readonly ageing = signal<AgeingRow[]>([]);
  protected readonly velocity = signal<VelocityRow[]>([]);
  protected readonly loading = signal(true);

  protected asOf = '';
  protected from = '';
  protected itemFilter = '';

  async ngOnInit(): Promise<void> {
    const today = new Date();
    const start = new Date(today.getFullYear(), today.getMonth() - 3, today.getDate());

    this.asOf = today.toISOString().slice(0, 10);
    this.from = start.toISOString().slice(0, 10);

    try {
      await this.refresh();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected async refresh(): Promise<void> {
    try {
      const [valuation, ageing, velocity] = await Promise.all([
        this.api.stockValuation(this.asOf, this.itemFilter || undefined),
        this.api.stockAgeing(this.asOf, this.itemFilter || undefined),
        this.api.stockVelocity(this.from, this.asOf),
      ]);

      this.valuation.set(valuation);
      this.ageing.set(ageing);
      this.velocity.set(velocity);
    } catch (error) {
      this.messages.showError(error);
    }
  }

  /** What the whole valuation comes to, which is the figure that meets the balance sheet. */
  protected totalValue(): number {
    return this.valuation().reduce((total, row) => total + row.value, 0);
  }

  /** How much of that total is still a guess. */
  protected totalEstimated(): number {
    return this.valuation().reduce((total, row) => total + row.estimatedValue, 0);
  }

  /** The band labels, taken from the first row so the header matches the data. */
  protected bands(): string[] {
    return this.ageing()[0]?.buckets.map((b) => b.label) ?? [];
  }

  protected bandQuantity(row: AgeingRow, label: string): number {
    return row.buckets.find((b) => b.label === label)?.quantity ?? 0;
  }

  protected describe(row: { description: string; descriptionArabic?: string | null }): string {
    return this.i18n.language() === 'ar' ? row.descriptionArabic || row.description : row.description;
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }
}

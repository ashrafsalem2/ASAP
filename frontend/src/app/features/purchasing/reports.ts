import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { OpenOrderRow, PurchaseAnalysisRow, VendorPerformanceRow } from '../../core/api/asap-api.models';
import { PurchasingService } from '../../core/api/purchasing.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * What is on order, who is letting us down, and where the money went.
 *
 * The judgment worth knowing about is in the vendor table. Lateness is averaged over the late
 * deliveries only — a vendor a fortnight late half the time and a fortnight early the rest would
 * otherwise read as punctual, and an erratic supplier is worse than a consistently slow one because
 * nothing can be planned around them. And a vendor who never promised a date is counted in its own
 * column rather than scored on time, or the supplier who commits to nothing tops the table.
 */
@Component({
  selector: 'asap-purchase-reports',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './reports.html',
})
export class PurchaseReports implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(PurchasingService);
  private readonly messages = inject(MessageService);

  protected readonly openOrders = signal<OpenOrderRow[]>([]);
  protected readonly vendors = signal<VendorPerformanceRow[]>([]);
  protected readonly analysis = signal<PurchaseAnalysisRow[]>([]);
  protected readonly loading = signal(true);

  protected from = '';
  protected to = '';
  protected overdueOnly = false;
  protected byItem = false;

  async ngOnInit(): Promise<void> {
    const today = new Date();
    const start = new Date(today.getFullYear(), today.getMonth() - 3, today.getDate());

    this.to = today.toISOString().slice(0, 10);
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
      const [open, vendors, analysis] = await Promise.all([
        this.api.openOrders(this.overdueOnly),
        this.api.vendorPerformance(this.from, this.to),
        this.api.purchaseAnalysis(this.from, this.to, this.byItem),
      ]);

      this.openOrders.set(open);
      this.vendors.set(vendors);
      this.analysis.set(analysis);
    } catch (error) {
      this.messages.showError(error);
    }
  }

  /** What is outstanding across every open order. */
  protected outstandingTotal(): number {
    return this.openOrders().reduce((sum, row) => sum + row.valueOutstanding, 0);
  }

  /** What was bought across the whole period. */
  protected spendTotal(): number {
    return this.analysis().reduce((sum, row) => sum + row.value, 0);
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }
}

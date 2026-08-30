import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MarginRow, OpenSalesOrderRow } from '../../core/api/asap-api.models';
import { SalesService } from '../../core/api/sales.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * What was sold, what it cost, and what is still to go out.
 *
 * The column worth understanding is estimated cost. A sale made from stock that had not arrived is
 * valued at a guess until the goods are received, so its margin is provisional — and a row whose
 * estimated cost is most of its cost is not telling you about your margin, it is telling you to come
 * back after the goods arrive.
 */
@Component({
  selector: 'asap-sales-reports',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './reports.html',
})
export class SalesReports implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(SalesService);
  private readonly messages = inject(MessageService);

  protected readonly byItem = signal<MarginRow[]>([]);
  protected readonly byCustomer = signal<MarginRow[]>([]);
  protected readonly open = signal<OpenSalesOrderRow[]>([]);
  protected readonly loading = signal(true);

  protected from = '';
  protected to = '';
  protected overdueOnly = false;

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
      const [items, customers, open] = await Promise.all([
        this.api.marginByItem(this.from, this.to),
        this.api.marginByCustomer(this.from, this.to),
        this.api.openSalesOrders(this.overdueOnly),
      ]);

      this.byItem.set(items);
      this.byCustomer.set(customers);
      this.open.set(open);
    } catch (error) {
      this.messages.showError(error);
    }
  }

  /** How much of the whole report's cost is still a guess. */
  protected estimatedTotal(): number {
    return this.byItem().reduce((sum, row) => sum + row.estimatedCost, 0);
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }
}

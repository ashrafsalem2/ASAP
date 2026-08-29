import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FiscalYearRow } from '../../core/api/asap-api.models';
import { FinanceService } from '../../core/api/finance.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * The financial years and the periods inside them.
 *
 * Read-only here on purpose. Opening and closing a period changes what may be posted, and that is
 * done from the year-end routine where the consequences are spelled out — not from a list where a
 * stray click would silently stop a shop trading.
 */
@Component({
  selector: 'asap-fiscal-periods',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './periods.html',
  styleUrl: './finance.scss',
})
export class FiscalPeriods implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(FinanceService);
  private readonly messages = inject(MessageService);

  protected readonly rows = signal<FiscalYearRow[]>([]);
  protected readonly loading = signal(true);

  async ngOnInit(): Promise<void> {
    try {
      this.rows.set(await this.api.fiscalYears());
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected name(row: { name: string; nameArabic?: string | null }): string {
    return this.i18n.language() === 'ar' && row.nameArabic ? row.nameArabic : row.name;
  }
}

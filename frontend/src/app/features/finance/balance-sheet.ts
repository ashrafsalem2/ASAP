import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  BalanceSheet,
  BalanceSheetRow,
  BalanceSheetSection,
} from '../../core/api/asap-api.models';
import { FinanceService } from '../../core/api/finance.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * What the company owned and owed on a given day.
 *
 * Equity carries lines with no account behind them: profit belongs to the owners from the moment
 * it is earned, but only reaches an equity account when the year-end transfer runs. Those lines
 * are marked, and the screen says why, because a figure in equity with no account number is
 * otherwise the sort of thing that makes a reader doubt the whole statement.
 */
@Component({
  selector: 'asap-balance-sheet',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './balance-sheet.html',
  styleUrl: './statements.scss',
})
export class BalanceSheetReport implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly finance = inject(FinanceService);
  private readonly messages = inject(MessageService);

  protected readonly sheet = signal<BalanceSheet | null>(null);
  protected readonly loading = signal(true);

  protected asAt = new Date().toISOString().slice(0, 10);
  protected includeAll = false;

  ngOnInit(): Promise<void> {
    return this.run();
  }

  protected t(key: TranslationKey): string {
    return this.i18n.translate(key);
  }

  protected sectionName(section: BalanceSheetSection): string {
    return this.t(`finance.statements.${section.category}` as TranslationKey);
  }

  /**
   * The line name.
   *
   * A computed line carries its own English and Arabic text from the server rather than an
   * account name, so the same lookup covers both.
   */
  protected nameOf(row: BalanceSheetRow): string {
    return this.i18n.language() === 'ar' && row.nameArabic ? row.nameArabic : row.name;
  }

  protected async run(): Promise<void> {
    this.loading.set(true);

    try {
      this.sheet.set(await this.finance.balanceSheet(this.asAt, this.includeAll));
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BranchPerformance as Report, BranchPerformanceRow } from '../../core/api/asap-api.models';
import { FinanceService } from '../../core/api/finance.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * The income statement, cut by branch.
 *
 * Every figure comes from the same ledger entries the company-wide statement is built from, so
 * the rows add up to it. That matters more than it sounds: a branch report that reconciles only
 * approximately gets argued with instead of acted on, and the argument is always about the report
 * rather than about the shop that is losing money.
 */
@Component({
  selector: 'asap-branch-performance',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './branch-performance.html',
  styleUrl: './finance.scss',
})
export class BranchPerformanceReport implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly finance = inject(FinanceService);
  private readonly messages = inject(MessageService);

  protected readonly report = signal<Report | null>(null);
  protected readonly loading = signal(true);

  protected from = '';
  protected to = '';

  async ngOnInit(): Promise<void> {
    const now = new Date();

    // The month to date, not the year. A branch report is read to decide something about this
    // month; a year-to-date figure buries a shop that has been losing money since April under
    // the three good months before it.
    this.from = this.iso(new Date(now.getFullYear(), now.getMonth(), 1));
    this.to = this.iso(now);

    await this.run();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected async run(): Promise<void> {
    if (!this.from || !this.to) {
      return;
    }

    this.loading.set(true);

    try {
      this.report.set(await this.finance.branchPerformance(this.from, this.to));
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  /** A branch's name in the reader's language, falling back to the one it has. */
  protected name(row: BranchPerformanceRow): string {
    return this.i18n.language() === 'ar' && row.nameArabic ? row.nameArabic : row.name;
  }

  /** A margin, or a dash where nothing was sold. */
  protected margin(value: number | null): string {
    return value === null ? '—' : `${this.i18n.total(value)}%`;
  }

  private iso(date: Date): string {
    const month = `${date.getMonth() + 1}`.padStart(2, '0');
    const day = `${date.getDate()}`.padStart(2, '0');

    return `${date.getFullYear()}-${month}-${day}`;
  }
}

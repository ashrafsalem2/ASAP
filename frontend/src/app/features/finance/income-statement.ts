import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  IncomeStatement,
  IncomeStatementRow,
  IncomeStatementSection,
} from '../../core/api/asap-api.models';
import { FinanceService } from '../../core/api/finance.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * What the company earned over a range.
 *
 * The comparison column is offered rather than shown by default. A company in its first year has
 * nothing to compare against, and a column of zeroes beside this year's figures reads as a
 * collapse rather than an absence.
 */
@Component({
  selector: 'asap-income-statement',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './income-statement.html',
  styleUrl: './statements.scss',
})
export class IncomeStatementReport implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly finance = inject(FinanceService);
  private readonly messages = inject(MessageService);

  protected readonly statement = signal<IncomeStatement | null>(null);
  protected readonly loading = signal(true);

  protected from = new Date(new Date().getFullYear(), 0, 1).toISOString().slice(0, 10);
  protected to = new Date().toISOString().slice(0, 10);
  protected comparePrevious = false;
  protected includeAll = false;

  ngOnInit(): Promise<void> {
    return this.run();
  }

  protected t(key: TranslationKey): string {
    return this.i18n.translate(key);
  }

  /** The heading for a block, which is a category name the server sent. */
  protected sectionName(section: IncomeStatementSection): string {
    return this.t(`finance.statements.${section.category}` as TranslationKey);
  }

  protected nameOf(row: IncomeStatementRow): string {
    return this.i18n.language() === 'ar' && row.nameArabic ? row.nameArabic : row.name;
  }

  /** Profit and loss are the same figure; only the label changes. */
  protected resultLabel(value: number): string {
    return this.t(value < 0 ? 'finance.statements.netLoss' : 'finance.statements.netProfit');
  }

  protected async run(): Promise<void> {
    this.loading.set(true);

    try {
      this.statement.set(
        await this.finance.incomeStatement(this.from, this.to, this.comparePrevious, this.includeAll),
      );
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

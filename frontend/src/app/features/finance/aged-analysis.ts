import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AgedAnalysis, AgedAnalysisRow, PartyKind } from '../../core/api/asap-api.models';
import { FinanceService } from '../../core/api/finance.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * What is owed, and how late it is.
 *
 * The bands are editable because a business selling on seven-day terms is not served by thirty-day
 * columns, and the report is only useful if its columns match the terms actually sold on.
 */
@Component({
  selector: 'asap-aged-analysis',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink],
  templateUrl: './aged-analysis.html',
  styleUrl: './statements.scss',
})
export class AgedAnalysisReport implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly finance = inject(FinanceService);
  private readonly messages = inject(MessageService);

  protected readonly report = signal<AgedAnalysis | null>(null);
  protected readonly loading = signal(true);

  protected kind: PartyKind = 'Customer';
  protected asAt = new Date().toISOString().slice(0, 10);
  protected bands = '30,60,90';

  ngOnInit(): Promise<void> {
    return this.run();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  /**
   * Turns a band code into a heading.
   *
   * Only <c>NotDue</c> needs translating; the rest are ranges such as <c>31-60</c>, which read the
   * same in both languages and would be worse spelled out.
   */
  protected bandLabel(code: string): string {
    if (code === 'NotDue') {
      return this.t('finance.aged.NotDue');
    }

    return code.startsWith('Over') ? `${code.slice(4)}+` : code;
  }

  protected nameOf(row: AgedAnalysisRow): string {
    return this.i18n.language() === 'ar' && row.nameArabic ? row.nameArabic : row.name;
  }

  /** The route to that party's account, so a figure can be chased from the report. */
  protected accountRoute(row: AgedAnalysisRow): string[] {
    return [this.kind === 'Customer' ? '/finance/customers' : '/finance/vendors', row.partyNo];
  }

  protected async run(): Promise<void> {
    this.loading.set(true);

    try {
      this.report.set(await this.finance.agedAnalysis(this.kind, this.asAt, this.bands));
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

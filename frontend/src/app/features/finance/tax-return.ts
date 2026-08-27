import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TaxReturn, TaxReturnLine } from '../../core/api/asap-api.models';
import { FinanceService } from '../../core/api/finance.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * What the company owes the tax authority for a period, or is owed by it.
 *
 * Built from the tax entries rather than the tax account balance, which is why zero-rated supplies
 * appear here at all: they move the account by nothing and still have to be declared.
 */
@Component({
  selector: 'asap-tax-return',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './tax-return.html',
  styleUrl: './statements.scss',
})
export class TaxReturnReport implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly finance = inject(FinanceService);
  private readonly messages = inject(MessageService);

  protected readonly report = signal<TaxReturn | null>(null);
  protected readonly loading = signal(true);

  protected from = TaxReturnReport.QuarterStart();
  protected to = new Date().toISOString().slice(0, 10);
  protected includeFiled = false;

  /** Most returns cover a quarter, so the screen opens on the current one. */
  private static QuarterStart(): string {
    const today = new Date();
    const month = Math.floor(today.getMonth() / 3) * 3;

    return new Date(Date.UTC(today.getFullYear(), month, 1)).toISOString().slice(0, 10);
  }

  ngOnInit(): Promise<void> {
    return this.run();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected describe(line: TaxReturnLine): string {
    return this.i18n.language() === 'ar' && line.descriptionArabic
      ? line.descriptionArabic
      : line.description;
  }

  protected directionLabel(direction: string): string {
    return this.t(`finance.tax.${direction}` as TranslationKey);
  }

  /**
   * Whether the net figure is money going out or coming back.
   *
   * A refund is ordinary for an exporter and for anyone who has just bought heavily, and reads
   * far better as a refund than as a negative payment.
   */
  protected isRefund(report: TaxReturn): boolean {
    return report.netPayable < 0;
  }

  protected async run(): Promise<void> {
    this.loading.set(true);

    try {
      this.report.set(await this.finance.taxReturn(this.from, this.to, this.includeFiled));
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

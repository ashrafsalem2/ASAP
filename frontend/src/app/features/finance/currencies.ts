import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CurrencyInfo, ExchangeRateInfo } from '../../core/api/asap-api.models';
import { CurrencyService } from '../../core/api/currency.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * The currencies the company deals in, and what each has been worth.
 *
 * The company's own currency is deliberately absent: it is on the company record and never needs
 * a rate against itself. A row for it here would invite somebody to give it one, at which point
 * every figure in the system quietly depends on whether that row says 1.
 *
 * The number worth showing beside each currency is today's rate, and whether there is one at all.
 * A currency with no rate for today is one that will refuse the next posting made in it, and
 * five seconds spent here is an afternoon saved later.
 */
@Component({
  selector: 'asap-currencies',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './currencies.html',
  styleUrl: './finance.scss',
})
export class Currencies implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(CurrencyService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly currencies = signal<CurrencyInfo[]>([]);
  protected readonly selected = signal<CurrencyInfo | null>(null);
  protected readonly rates = signal<ExchangeRateInfo[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly includeInactive = signal(false);

  protected newStartingDate = '';
  protected newBaseAmount = '';
  protected newCurrencyAmount = '1';

  async ngOnInit(): Promise<void> {
    this.newStartingDate = new Date().toISOString().slice(0, 10);

    await this.reload();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canWrite(): boolean {
    return this.auth.can('Finance.Currency.Update');
  }

  protected name(currency: CurrencyInfo): string {
    return this.i18n.language() === 'ar' && currency.nameArabic ? currency.nameArabic : currency.name;
  }

  protected async select(currency: CurrencyInfo): Promise<void> {
    this.selected.set(currency);
    this.newBaseAmount = '';
    this.newCurrencyAmount = '1';

    try {
      this.rates.set(await this.api.rates(currency.code));
    } catch (error) {
      this.messages.showError(error);
    }
  }

  protected async toggleInactive(): Promise<void> {
    this.includeInactive.update((on) => !on);
    await this.reload();
  }

  protected async addRate(): Promise<void> {
    const currency = this.selected();
    const baseAmount = Number(this.newBaseAmount);
    const currencyAmount = Number(this.newCurrencyAmount);

    if (!currency || !this.newStartingDate || !(baseAmount > 0) || !(currencyAmount > 0)) {
      return;
    }

    this.busy.set(true);

    try {
      await this.api.saveRate(currency.code, {
        startingDate: this.newStartingDate,
        baseAmount,
        currencyAmount,
      });

      this.messages.showSuccess(this.t('finance.currencies.rateAdded', { code: currency.code }));
      this.newBaseAmount = '';

      await this.reload();
      await this.select(this.currencies().find((c) => c.code === currency.code) ?? currency);
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  private async reload(): Promise<void> {
    this.loading.set(true);

    try {
      this.currencies.set(await this.api.list(this.includeInactive()));
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

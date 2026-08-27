import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Entitlements as EntitlementsReport } from '../../core/api/asap-api.models';
import { HrService } from '../../core/api/hr.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * What the company owes its staff, whether or not anybody has resigned.
 *
 * Unused leave and end-of-service are earned by the day and paid at the end, which makes them
 * easy to leave off the books until somebody walks out — and a company that does that reports a
 * profit every year that it does not have, then meets the whole bill in the year it finally
 * counts. This screen exists so nobody has to be surprised by it.
 */
@Component({
  selector: 'asap-entitlements',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './entitlements.html',
  styleUrl: './hr.scss',
})
export class Entitlements implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly hr = inject(HrService);
  private readonly messages = inject(MessageService);

  protected readonly report = signal<EntitlementsReport | null>(null);
  protected readonly loading = signal(true);

  protected asAt = '';

  async ngOnInit(): Promise<void> {
    const now = new Date();
    const month = `${now.getMonth() + 1}`.padStart(2, '0');
    const day = `${now.getDate()}`.padStart(2, '0');

    this.asAt = `${now.getFullYear()}-${month}-${day}`;

    await this.run();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected async run(): Promise<void> {
    this.loading.set(true);

    try {
      this.report.set(await this.hr.entitlements(this.asAt || undefined));
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  /** Years of service to one decimal. Two would imply a precision the input does not have. */
  protected years(value: number): string {
    return value.toFixed(1);
  }
}

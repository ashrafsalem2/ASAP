import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PartyKind, RevaluationResult } from '../../core/api/asap-api.models';
import { AuthService } from '../../core/auth/auth.service';
import { FinanceService } from '../../core/api/finance.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * Restating open foreign balances at the rate on the day being closed.
 *
 * Preview and post are two buttons rather than one, because the second changes a balance sheet
 * somebody may already have reported. All five figures behind each difference are on the row: a
 * revaluation nobody can reproduce is one nobody signs off.
 */
@Component({
  selector: 'asap-currency-revaluation',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './revaluation.html',
  styleUrl: './finance.scss',
})
export class CurrencyRevaluation implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(FinanceService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly run = signal<RevaluationResult | null>(null);
  protected readonly posted = signal(false);
  protected readonly busy = signal(false);

  protected asAt = '';
  protected kind: PartyKind = 'Customer';

  async ngOnInit(): Promise<void> {
    // The end of last month, which is what somebody opening this screen almost always wants.
    const today = new Date();
    const endOfLastMonth = new Date(today.getFullYear(), today.getMonth(), 0);

    this.asAt = endOfLastMonth.toISOString().slice(0, 10);

    await this.preview();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canPost(): boolean {
    return this.auth.can('Finance.Journal.Post');
  }

  /** A loss to the company, which is the sign the difference is written in. */
  protected isLoss(difference: number): boolean {
    return difference > 0;
  }

  protected async preview(): Promise<void> {
    if (!this.asAt) {
      return;
    }

    this.busy.set(true);
    this.posted.set(false);

    try {
      this.run.set(await this.api.revaluationPreview(this.asAt, this.kind));
    } catch (error) {
      this.run.set(null);
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async post(): Promise<void> {
    const preview = this.run();

    if (!preview || preview.rows.length === 0) {
      return;
    }

    this.busy.set(true);

    try {
      const result = await this.api.postRevaluation(this.asAt, this.kind);

      this.run.set(result);
      this.posted.set(true);

      this.messages.showSuccess(
        this.t('finance.revaluation.posted', {
          Count: result.rows.length,
          Transaction: result.transactionNo ?? 0,
        }),
      );
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }
}

import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { GlEntry } from '../../core/api/asap-api.models';
import { FinanceService } from '../../core/api/finance.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

@Component({
  selector: 'asap-ledger-entries',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  template: `
    <div class="page">
      <h1>{{ t('finance.entries.title') }}</h1>

      <section class="panel">
        <div class="panel__body controls">
          <div class="field">
            <label class="field__label" for="accountNo">
              {{ t('finance.entries.filterAccount') }}
            </label>
            <input id="accountNo" class="input code" [(ngModel)]="accountNo" />
          </div>

          <button type="button" class="button" [disabled]="loading()" (click)="load()">
            @if (loading()) {
              <span class="spinner"></span>
            }
            {{ t('finance.trialBalance.run') }}
          </button>
        </div>

        <div class="table-scroll">
          <table class="table">
            <thead>
              <tr>
                <th>{{ t('finance.entries.date') }}</th>
                <th class="numeric">{{ t('finance.entries.transaction') }}</th>
                <th>{{ t('finance.accounts.no') }}</th>
                <th>{{ t('finance.journal.description') }}</th>
                <th class="numeric">{{ t('finance.journal.debit') }}</th>
                <th class="numeric">{{ t('finance.journal.credit') }}</th>
                <th>{{ t('finance.entries.document') }}</th>
                <th>{{ t('finance.entries.source') }}</th>
                @if (canReverse()) {
                  <th></th>
                }
              </tr>
            </thead>

            <tbody>
              @for (entry of entries(); track entry.id) {
                <tr>
                  <td class="code">{{ entry.postingDate }}</td>
                  <td class="numeric">{{ entry.transactionNo }}</td>
                  <td class="code">{{ entry.accountNo }}</td>
                  <td class="description">{{ entry.description }}</td>
                  <td class="numeric">{{ i18n.amount(entry.debitAmount) }}</td>
                  <td class="numeric">{{ i18n.amount(entry.creditAmount) }}</td>
                  <td class="code">{{ entry.documentNo }}</td>
                  <td><span class="tag tag--muted">{{ entry.sourceCode }}</span></td>

                  @if (canReverse()) {
                    <td>
                      <button
                        type="button"
                        class="button button--quiet"
                        (click)="startReversal(entry.transactionNo)"
                      >
                        {{ t('finance.entries.reverse') }}
                      </button>
                    </td>
                  }
                </tr>
              } @empty {
                <tr>
                  <td colspan="9" class="empty">{{ t('common.nothingHere') }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </section>
    </div>

    @if (reversing(); as transactionNo) {
      <div class="scrim" (click)="cancelReversal()"></div>

      <div class="dialog panel" role="dialog" aria-modal="true">
        <div class="panel__header">
          <h2>{{ t('finance.entries.confirmReverse') }} {{ transactionNo }}</h2>
        </div>

        <div class="panel__body">
          <!-- A reason is asked for, not optional. It goes onto the reversing entries and into the
               audit log, and "why was this reversed" is the first question anyone asks when they
               find a correction on an account months later. -->
          <div class="field">
            <label class="field__label" for="reason">{{ t('finance.entries.reverseReason') }}</label>
            <input id="reason" class="input" [(ngModel)]="reason" />
          </div>
        </div>

        <div class="panel__body dialog__actions">
          <button type="button" class="button" (click)="cancelReversal()">
            {{ t('finance.entries.cancel') }}
          </button>

          <button
            type="button"
            class="button button--danger"
            [disabled]="!reason.trim() || busy()"
            (click)="confirmReversal(transactionNo)"
          >
            {{ t('finance.entries.confirmReverse') }}
          </button>
        </div>
      </div>
    }
  `,
  styles: `
    .controls {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      gap: 1rem;
      border-bottom: 1px solid var(--border);
    }

    .description {
      white-space: normal;
      min-width: 18rem;
    }

    .scrim {
      position: fixed;
      inset: 0;
      z-index: 50;
      background: rgb(0 0 0 / 35%);
    }

    .dialog {
      position: fixed;
      z-index: 51;
      top: 50%;
      left: 50%;
      transform: translate(-50%, -50%);
      width: min(28rem, calc(100vw - 2rem));
      box-shadow: var(--shadow-raised);
    }

    .dialog__actions {
      display: flex;
      justify-content: flex-end;
      gap: 0.75rem;
      border-top: 1px solid var(--border);
    }
  `,
})
export class LedgerEntries implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly finance = inject(FinanceService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly entries = signal<GlEntry[]>([]);
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly reversing = signal<number | null>(null);

  protected accountNo = '';
  protected reason = '';

  ngOnInit(): Promise<void> {
    return this.load();
  }

  protected t(key: TranslationKey): string {
    return this.i18n.translate(key);
  }

  /** Hides the reverse action from anyone who cannot use it, rather than showing a dead button. */
  protected canReverse(): boolean {
    return this.auth.can('Finance.Entry.Reverse');
  }

  protected async load(): Promise<void> {
    this.loading.set(true);

    try {
      this.entries.set(await this.finance.entries({ accountNo: this.accountNo || undefined }));
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected startReversal(transactionNo: number): void {
    this.reason = '';
    this.reversing.set(transactionNo);
  }

  protected cancelReversal(): void {
    this.reversing.set(null);
  }

  protected async confirmReversal(transactionNo: number): Promise<void> {
    this.messages.clear();
    this.busy.set(true);

    try {
      const receipt = await this.finance.reverse(transactionNo, this.reason.trim());

      this.messages.showSuccess(
        `${this.t('finance.entries.reverse')} ${transactionNo}`,
        `${receipt.entryCount} → ${receipt.transactionNo}`,
      );

      this.reversing.set(null);
      await this.load();
    } catch (error) {
      // Reversing twice, or into a closed period, comes back as a proper message with its own
      // resolution. Showing it is all this needs to do.
      this.messages.showError(error, this.t('finance.entries.reverse'));
    } finally {
      this.busy.set(false);
    }
  }
}

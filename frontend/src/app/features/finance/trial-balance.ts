import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TrialBalance as TrialBalanceReport, TrialBalanceRow } from '../../core/api/asap-api.models';
import { FinanceService } from '../../core/api/finance.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

@Component({
  selector: 'asap-trial-balance',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  template: `
    <div class="page">
      <h1>{{ t('finance.trialBalance.title') }}</h1>

      <section class="panel">
        <div class="panel__body controls">
          <div class="field">
            <label class="field__label" for="from">{{ t('finance.trialBalance.from') }}</label>
            <input id="from" type="date" class="input" [(ngModel)]="from" />
          </div>

          <div class="field">
            <label class="field__label" for="to">{{ t('finance.trialBalance.to') }}</label>
            <input id="to" type="date" class="input" [(ngModel)]="to" />
          </div>

          <label class="controls__toggle">
            <input type="checkbox" [(ngModel)]="includeAll" />
            {{ t('finance.trialBalance.showAll') }}
          </label>

          <button type="button" class="button button--primary" [disabled]="loading()" (click)="run()">
            @if (loading()) {
              <span class="spinner"></span>
            }
            {{ t('finance.trialBalance.run') }}
          </button>
        </div>

        @if (report(); as data) {
          <!-- Stated rather than assumed. Every row reached the ledger through the posting engine,
               which refuses anything unbalanced, so a difference here is not a rounding artefact --
               it means something wrote the ledger another way, and the report says so plainly. -->
          <div
            class="panel__body balance-note"
            [class.balance-note--bad]="!data.isBalanced"
          >
            @if (data.isBalanced) {
              <span class="tag tag--positive">{{ t('finance.trialBalance.balanced') }}</span>
            } @else {
              <p class="balance-note__warning">{{ t('finance.trialBalance.notBalanced') }}</p>
            }
          </div>

          <div class="table-scroll">
            <table class="table">
              <thead>
                <tr>
                  <th>{{ t('finance.accounts.no') }}</th>
                  <th>{{ t('finance.accounts.name') }}</th>
                  <th class="numeric">{{ t('finance.trialBalance.opening') }}</th>
                  <th class="numeric">{{ t('finance.journal.debit') }}</th>
                  <th class="numeric">{{ t('finance.journal.credit') }}</th>
                  <th class="numeric">{{ t('finance.trialBalance.closing') }}</th>
                </tr>
              </thead>

              <tbody>
                @for (row of data.rows; track row.accountNo) {
                  <tr [class.row--structural]="row.accountType !== 'Posting'">
                    <td class="code">{{ row.accountNo }}</td>
                    <td>
                      <span [style.padding-inline-start.rem]="row.indentation * 1.25">
                        {{ nameOf(row) }}
                      </span>
                    </td>
                    <td class="numeric">{{ i18n.amount(row.openingBalance) }}</td>
                    <td class="numeric">{{ i18n.amount(row.periodDebit) }}</td>
                    <td class="numeric">{{ i18n.amount(row.periodCredit) }}</td>
                    <td class="numeric">{{ i18n.amount(row.closingBalance) }}</td>
                  </tr>
                } @empty {
                  <tr>
                    <td colspan="6" class="empty">{{ t('common.nothingHere') }}</td>
                  </tr>
                }
              </tbody>

              <tfoot>
                <tr>
                  <td colspan="3">{{ data.currencyCode }}</td>
                  <td class="numeric">{{ i18n.amount(data.totalDebit) }}</td>
                  <td class="numeric">{{ i18n.amount(data.totalCredit) }}</td>
                  <td></td>
                </tr>
              </tfoot>
            </table>
          </div>
        }
      </section>
    </div>
  `,
  styles: `
    .controls {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      gap: 1rem;
      border-bottom: 1px solid var(--border);
    }

    .controls__toggle {
      display: flex;
      align-items: center;
      gap: 0.4375rem;
      font-size: 0.875rem;
      color: var(--ink-soft);
      padding-bottom: 0.5rem;
    }

    .balance-note {
      padding-block: 0.75rem;
      border-bottom: 1px solid var(--border);
    }

    .balance-note--bad {
      background: var(--negative-soft);
    }

    .balance-note__warning {
      margin: 0;
      color: var(--negative);
      font-size: 0.875rem;
      max-width: 70ch;
    }

    .row--structural {
      font-weight: 600;
      background: var(--surface-sunken);
    }
  `,
})
export class TrialBalance implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly finance = inject(FinanceService);
  private readonly messages = inject(MessageService);

  protected readonly report = signal<TrialBalanceReport | null>(null);
  protected readonly loading = signal(false);

  // Defaults to the year so far, which is what someone opening the report almost always wants and
  // saves them constructing a range to see anything at all.
  protected from = `${new Date().getFullYear()}-01-01`;
  protected to = new Date().toISOString().slice(0, 10);
  protected includeAll = false;

  ngOnInit(): Promise<void> {
    return this.run();
  }

  protected t(key: TranslationKey): string {
    return this.i18n.translate(key);
  }

  protected nameOf(row: TrialBalanceRow): string {
    return this.i18n.language() === 'ar' && row.nameArabic ? row.nameArabic : row.name;
  }

  protected async run(): Promise<void> {
    this.loading.set(true);

    try {
      this.report.set(await this.finance.trialBalance(this.from, this.to, this.includeAll));
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

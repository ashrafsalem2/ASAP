import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { GlAccount } from '../../core/api/asap-api.models';
import { FinanceService } from '../../core/api/finance.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

@Component({
  selector: 'asap-chart-of-accounts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <h1>{{ t('finance.accounts.title') }}</h1>

      <section class="panel">
        @if (loading()) {
          <p class="empty"><span class="spinner"></span> {{ t('common.loading') }}</p>
        } @else if (accounts().length === 0) {
          <p class="empty">{{ t('common.nothingHere') }}</p>
        } @else {
          <div class="table-scroll">
            <table class="table">
              <thead>
                <tr>
                  <th>{{ t('finance.accounts.no') }}</th>
                  <th>{{ t('finance.accounts.name') }}</th>
                  <th>{{ t('finance.accounts.category') }}</th>
                  <th class="numeric">{{ t('finance.accounts.balance') }}</th>
                </tr>
              </thead>
              <tbody>
                @for (account of accounts(); track account.id) {
                  <tr [class.account--structural]="account.accountType !== 'Posting'">
                    <td class="code">{{ account.no }}</td>

                    <td>
                      <!-- Indentation comes from the account itself, so the chart on screen has the
                           same shape as the chart the accountant designed. -->
                      <span [style.padding-inline-start.rem]="account.indentation * 1.25">
                        {{ nameOf(account) }}
                      </span>

                      @if (!account.allowsDirectPosting && account.accountType === 'Posting') {
                        <span class="tag tag--muted account__system">
                          {{ t('finance.accounts.systemOnly') }}
                        </span>
                      }
                    </td>

                    <td>
                      <span class="tag">{{ account.category }}</span>
                    </td>

                    <td class="numeric">{{ i18n.amount(account.balance) }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </section>
    </div>
  `,
  styles: `
    /* Headings and totals shape the report rather than holding a balance, so they read as
       structure rather than as data. */
    .account--structural {
      font-weight: 600;
      background: var(--surface-sunken);
    }

    .account__system {
      margin-inline-start: 0.5rem;
    }
  `,
})
export class ChartOfAccounts implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly finance = inject(FinanceService);
  private readonly messages = inject(MessageService);

  protected readonly accounts = signal<GlAccount[]>([]);
  protected readonly loading = signal(true);

  async ngOnInit(): Promise<void> {
    try {
      this.accounts.set(await this.finance.accounts());
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected t(key: TranslationKey): string {
    return this.i18n.translate(key);
  }

  protected nameOf(account: GlAccount): string {
    return this.i18n.language() === 'ar' && account.nameArabic ? account.nameArabic : account.name;
  }
}

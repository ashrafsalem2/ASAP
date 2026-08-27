import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Party, PartyKind } from '../../core/api/asap-api.models';
import { FinanceService } from '../../core/api/finance.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * Customers or vendors, with what each owes.
 *
 * One component serves both, told which it is by the route. They carry the same information and
 * obey the same rules; a second copy would be the one that stops matching the first.
 */
@Component({
  selector: 'asap-parties',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <div class="page">
      <h1>{{ t(kind() === 'Customer' ? 'finance.parties.customers' : 'finance.parties.vendors') }}</h1>

      <section class="panel">
        @if (loading()) {
          <p class="empty"><span class="spinner"></span> {{ t('common.loading') }}</p>
        } @else {
          <div class="table-scroll">
            <table class="table">
              <thead>
                <tr>
                  <th>{{ t('finance.parties.no') }}</th>
                  <th>{{ t('finance.parties.name') }}</th>
                  <th>{{ t('finance.parties.terms') }}</th>
                  <th class="numeric">{{ t('finance.parties.creditLimit') }}</th>
                  <th class="numeric">{{ t('finance.parties.balance') }}</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                @for (party of parties(); track party.no) {
                  <tr>
                    <td class="code">{{ party.no }}</td>
                    <td>
                      {{ nameOf(party) }}

                      @if (party.isBlocked) {
                        <span class="tag tag--negative party__flag">
                          {{ t('finance.parties.blocked') }}
                        </span>
                      }
                    </td>
                    <td>{{ terms(party) }}</td>
                    <td class="numeric party__limit">
                      @if (party.creditLimit > 0) {
                        {{ i18n.amount(party.creditLimit) }}
                      } @else {
                        <span class="party__none">{{ t('finance.parties.noLimit') }}</span>
                      }
                    </td>
                    <td class="numeric" [class.party__over]="party.isOverLimit">
                      {{ i18n.total(party.balance) }}

                      <!-- Flagged on the list, because the whole point of a limit is being told
                           before somebody takes the next order rather than after. -->
                      @if (party.isOverLimit) {
                        <span class="tag tag--negative">{{ t('finance.parties.overLimit') }}</span>
                      }
                    </td>
                    <td>
                      <a class="button button--quiet" [routerLink]="['./', party.no]">
                        {{ t('finance.parties.account') }}
                      </a>
                    </td>
                  </tr>
                } @empty {
                  <tr>
                    <td colspan="6" class="empty">{{ t('common.nothingHere') }}</td>
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
    .party__flag {
      margin-inline-start: 0.5rem;
    }

    .party__over {
      color: var(--negative);
      font-weight: 600;
    }

    .party__over .tag {
      margin-inline-start: 0.5rem;
    }

    .party__none {
      color: var(--text-muted);
    }
  `,
})
export class Parties implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly finance = inject(FinanceService);
  private readonly messages = inject(MessageService);
  private readonly route = inject(ActivatedRoute);

  protected readonly parties = signal<Party[]>([]);
  protected readonly loading = signal(true);
  protected readonly kind = signal<PartyKind>('Customer');

  async ngOnInit(): Promise<void> {
    this.kind.set((this.route.snapshot.data['kind'] as PartyKind) ?? 'Customer');

    try {
      this.parties.set(await this.finance.parties(this.kind()));
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected nameOf(party: Party): string {
    return this.i18n.language() === 'ar' && party.nameArabic ? party.nameArabic : party.name;
  }

  /** Zero days is not "0 days", it is cash on delivery, and reads as nonsense otherwise. */
  protected terms(party: Party): string {
    return party.paymentTermsDays === 0
      ? this.t('finance.parties.onDelivery')
      : this.t('finance.parties.termsDays', { Days: party.paymentTermsDays });
  }
}

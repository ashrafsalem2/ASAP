import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { StockOnHandRow } from '../../core/api/asap-api.models';
import { InventoryService } from '../../core/api/inventory.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

@Component({
  selector: 'asap-stock-on-hand',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <div class="header">
        <h1>{{ t('inventory.stock.title') }}</h1>

        @if (canSettle() && hasNegative()) {
          <button type="button" class="button button--primary" [disabled]="settling()" (click)="settle()">
            @if (settling()) {
              <span class="spinner"></span>
              {{ t('inventory.stock.settling') }}
            } @else {
              {{ t('inventory.stock.settle') }}
            }
          </button>
        }
      </div>

      <!-- Shown only when there is something below zero, because otherwise it is an explanation of
           a situation the reader is not in. -->
      @if (hasNegative()) {
        <p class="page__intro warning">{{ t('inventory.stock.negativeNote') }}</p>
      }

      <section class="panel">
        @if (loading()) {
          <p class="empty"><span class="spinner"></span> {{ t('common.loading') }}</p>
        } @else {
          <div class="table-scroll">
            <table class="table">
              <thead>
                <tr>
                  <th>{{ t('inventory.items.no') }}</th>
                  <th>{{ t('inventory.items.description') }}</th>
                  <th>{{ t('inventory.stock.location') }}</th>
                  <th class="numeric">{{ t('inventory.stock.quantity') }}</th>
                </tr>
              </thead>
              <tbody>
                @for (row of rows(); track row.itemNo + row.locationCode) {
                  <tr>
                    <td class="code">{{ row.itemNo }}</td>
                    <td>{{ describe(row) }}</td>
                    <td class="code">{{ row.locationCode }}</td>
                    <td class="numeric" [class.negative]="row.isNegative">
                      {{ quantity(row.quantity) }}

                      @if (row.isNegative) {
                        <span class="tag tag--negative">{{ t('inventory.stock.negative') }}</span>
                      }
                    </td>
                  </tr>
                } @empty {
                  <tr>
                    <td colspan="4" class="empty">{{ t('common.nothingHere') }}</td>
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
    .header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 1rem;
      flex-wrap: wrap;
    }

    .warning {
      padding: 0.75rem 1rem;
      border-radius: var(--radius);
      background: var(--caution-soft);
      color: var(--caution);
      max-width: none;
    }

    .negative {
      color: var(--negative);
      font-weight: 600;
    }

    .negative .tag {
      margin-inline-start: 0.5rem;
    }
  `,
})
export class StockOnHand implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly inventory = inject(InventoryService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly rows = signal<StockOnHandRow[]>([]);
  protected readonly loading = signal(true);
  protected readonly settling = signal(false);

  /** Whether anything is below zero, which decides whether settling is worth offering at all. */
  protected readonly hasNegative = computed(() => this.rows().some((row) => row.isNegative));

  ngOnInit(): Promise<void> {
    return this.load();
  }

  protected t(key: TranslationKey): string {
    return this.i18n.translate(key);
  }

  protected canSettle(): boolean {
    return this.auth.can('Inventory.Stock.Post');
  }

  /** The item as the reader would name it, which is not always the English column. */
  protected describe(row: StockOnHandRow): string {
    return this.i18n.language() === 'ar' && row.descriptionArabic
      ? row.descriptionArabic
      : row.description;
  }

  protected quantity(value: number): string {
    return new Intl.NumberFormat(this.i18n.locale(), { maximumFractionDigits: 5 }).format(value);
  }

  protected async settle(): Promise<void> {
    this.messages.clear();
    this.settling.set(true);

    try {
      const result = await this.inventory.settle();

      // Every settlement says what it corrected and by how much. Showing them is the difference
      // between a routine that quietly adjusts the books and one whose work can be checked.
      this.messages.showAll(result.messages ?? []);

      if (result.applicationsSettled === 0) {
        this.messages.showSuccess(this.t('inventory.stock.settle'), '0');
      }

      await this.load();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.settling.set(false);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      this.rows.set(await this.inventory.onHand());
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

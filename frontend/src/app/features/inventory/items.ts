import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { Item } from '../../core/api/asap-api.models';
import { InventoryService } from '../../core/api/inventory.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

@Component({
  selector: 'asap-items',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <h1>{{ t('inventory.items.title') }}</h1>

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
                  <th>{{ t('inventory.items.costing') }}</th>
                  <th class="numeric">{{ t('inventory.items.unitCost') }}</th>
                  <th class="numeric">{{ t('inventory.items.unitPrice') }}</th>
                  <th class="numeric">{{ t('inventory.items.onHand') }}</th>
                  <th class="numeric">{{ t('inventory.items.reorderPoint') }}</th>
                </tr>
              </thead>
              <tbody>
                @for (item of items(); track item.no) {
                  <tr>
                    <td class="code">{{ item.no }}</td>
                    <td>
                      {{ nameOf(item) }}

                      <!-- Shown only where the item overrides the company, because that is the
                           exception worth noticing on a list of two thousand items. -->
                      @if (item.allowNegativeInventory === true) {
                        <span class="tag tag--muted item__flag">
                          {{ t('inventory.items.allowsNegative') }}
                        </span>
                      }
                    </td>
                    <td><span class="tag">{{ item.costingMethod }}</span></td>
                    <td class="numeric">{{ i18n.amount(item.unitCost) }}</td>
                    <td class="numeric">{{ i18n.amount(item.unitPrice) }}</td>
                    <td class="numeric" [class.item__low]="isLow(item)">
                      {{ quantity(item.quantityOnHand) }}
                    </td>
                    <td class="numeric">{{ quantity(item.reorderPoint) }}</td>
                  </tr>
                } @empty {
                  <tr>
                    <td colspan="7" class="empty">{{ t('common.nothingHere') }}</td>
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
    .item__flag {
      margin-inline-start: 0.5rem;
    }

    /* An item at or below its reorder point is the one thing on this screen somebody is looking
       for, so it is marked rather than left to be found by reading two columns against each
       other. */
    .item__low {
      color: var(--caution);
      font-weight: 600;
    }
  `,
})
export class Items implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly inventory = inject(InventoryService);
  private readonly messages = inject(MessageService);

  protected readonly items = signal<Item[]>([]);
  protected readonly loading = signal(true);

  async ngOnInit(): Promise<void> {
    try {
      this.items.set(await this.inventory.items());
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected t(key: TranslationKey): string {
    return this.i18n.translate(key);
  }

  protected nameOf(item: Item): string {
    return this.i18n.language() === 'ar' && item.descriptionArabic
      ? item.descriptionArabic
      : item.description;
  }

  protected isLow(item: Item): boolean {
    return item.reorderPoint > 0 && item.quantityOnHand <= item.reorderPoint;
  }

  /**
   * Formats a quantity without its trailing zeros.
   *
   * Stock is stored to five decimals because goods are sold in fractions of a unit, but printing
   * "40.00000" on every row of a list makes the column unreadable for the sake of a precision that
   * is almost never used.
   */
  protected quantity(value: number): string {
    return new Intl.NumberFormat(this.i18n.locale(), { maximumFractionDigits: 5 }).format(value);
  }
}

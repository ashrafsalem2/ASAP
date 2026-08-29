import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { StockLocation } from '../../core/api/asap-api.models';
import { InventoryService } from '../../core/api/inventory.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * The places stock is held.
 *
 * The two flags are what a reader is here for. A location that is not sellable holds stock nobody
 * can sell off it — a warehouse behind a shop — and an in-transit one holds what has left one place
 * and not arrived at the next, which is the stock a transfer is carrying right now.
 */
@Component({
  selector: 'asap-locations',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './locations.html',
})
export class Locations implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(InventoryService);
  private readonly messages = inject(MessageService);

  protected readonly rows = signal<StockLocation[]>([]);
  protected readonly loading = signal(true);

  async ngOnInit(): Promise<void> {
    try {
      this.rows.set(await this.api.locations());
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected name(row: { name: string; nameArabic?: string | null }): string {
    return this.i18n.language() === 'ar' && row.nameArabic ? row.nameArabic : row.name;
  }
}

import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { PosStation } from '../../core/api/asap-api.models';
import { PosService } from '../../core/api/pos.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * The tills, and whether each has a session open.
 *
 * The open session is the column worth having. A till left open overnight is the commonest reason
 * a day's takings do not add up, and it is invisible from anywhere else.
 */
@Component({
  selector: 'asap-stations',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './stations.html',
  styleUrl: './pos.scss',
})
export class Stations implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(PosService);
  private readonly messages = inject(MessageService);

  protected readonly rows = signal<PosStation[]>([]);
  protected readonly loading = signal(true);

  async ngOnInit(): Promise<void> {
    try {
      this.rows.set(await this.api.stations());
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

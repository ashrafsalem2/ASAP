import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { PosDeviceInfo, PosStation, StationReadiness } from '../../core/api/asap-api.models';
import { PosService } from '../../core/api/pos.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * The tills, whether each has a session open, and what each needs installed on it.
 *
 * Two questions on one page because they are asked together. "Is this till still open" is what a
 * manager asks at the end of the day — a till left open overnight is the commonest reason a day's
 * takings do not add up, and it is invisible from anywhere else. "What do I have to install on
 * it" is what somebody asks when opening a shop, and for most tills the answer is nothing at all.
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
  protected readonly devices = signal<PosDeviceInfo[]>([]);
  protected readonly readiness = signal<StationReadiness | null>(null);
  protected readonly selected = signal<PosStation | null>(null);
  protected readonly loading = signal(true);

  async ngOnInit(): Promise<void> {
    try {
      const stations = await this.api.stations();

      this.rows.set(stations);

      if (stations.length > 0) {
        await this.select(stations[0]);
      }
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

  protected async select(station: PosStation): Promise<void> {
    this.selected.set(station);

    try {
      const [devices, readiness] = await Promise.all([
        this.api.devices(station.code),
        this.api.readiness(station.code),
      ]);

      this.devices.set(devices);
      this.readiness.set(readiness);
    } catch (error) {
      this.messages.showError(error);
    }
  }
}

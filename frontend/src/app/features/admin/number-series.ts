import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NumberSeriesInfo, NumberSeriesLineInfo } from '../../core/api/asap-api.models';
import { NumberSeriesService } from '../../core/api/number-series.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * The series every document number comes out of.
 *
 * A series whose last line ends in December stops the shop trading on the first of January.
 * Adding next year's line takes ten seconds and finding out you needed to takes a morning, which
 * is why the number worth showing on this screen is how many each line has left.
 */
@Component({
  selector: 'asap-number-series',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './number-series.html',
  styleUrl: './admin.scss',
})
export class NumberSeriesScreen implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(NumberSeriesService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly series = signal<NumberSeriesInfo[]>([]);
  protected readonly selected = signal<NumberSeriesInfo | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);

  protected newStartingDate = '';
  protected newStartingNumber = '';
  protected newEndingNumber = '';

  async ngOnInit(): Promise<void> {
    const nextYear = new Date().getFullYear() + 1;

    this.newStartingDate = `${nextYear}-01-01`;

    await this.reload();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canWrite(): boolean {
    return this.auth.can('Platform.NumberSeries.Update');
  }

  protected select(series: NumberSeriesInfo): void {
    this.selected.set(series);
    this.newStartingNumber = '';
    this.newEndingNumber = '';
  }

  /** Whether a line is close enough to running out to be worth colouring. */
  protected running(line: NumberSeriesLineInfo): boolean {
    if (line.remaining === null) {
      return false;
    }

    const threshold = line.warnWhenRemainingBelow ?? 0;

    return threshold > 0 && line.remaining <= threshold;
  }

  protected async addLine(): Promise<void> {
    const series = this.selected();

    if (!series || !this.newStartingDate || !this.newStartingNumber.trim()) {
      return;
    }

    this.busy.set(true);

    try {
      await this.api.save({
        code: series.code,
        description: series.description,
        descriptionArabic: series.descriptionArabic,
        allowGaps: series.allowGaps,
        allowManualEntry: series.allowManualEntry,
        enforceDateOrder: series.enforceDateOrder,
        isActive: series.isActive,

        // Every line, not just the new one: the endpoint takes the whole set, and sending only
        // the addition would close every line already there.
        lines: [
          ...series.lines.map((line) => ({
            startingDate: line.startingDate,
            startingNumber: line.startingNumber,
            endingNumber: line.endingNumber,
            increment: line.increment,
            warnWhenRemainingBelow: line.warnWhenRemainingBelow,
            isOpen: line.isOpen,
          })),
          {
            startingDate: this.newStartingDate,
            startingNumber: this.newStartingNumber.trim(),
            endingNumber: this.newEndingNumber.trim() || null,
            increment: 1,
            warnWhenRemainingBelow: null,
            isOpen: true,
          },
        ],
      });

      this.messages.showSuccess(this.t('admin.numbers.added', { code: series.code }));
      this.newStartingNumber = '';
      this.newEndingNumber = '';
      await this.reload();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  private async reload(): Promise<void> {
    this.loading.set(true);

    try {
      const list = await this.api.list();

      this.series.set(list);

      const current = this.selected();

      if (current) {
        this.selected.set(list.find((s) => s.code === current.code) ?? null);
      }
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

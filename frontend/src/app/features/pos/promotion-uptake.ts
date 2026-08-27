import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PromotionUptake } from '../../core/api/asap-api.models';
import { PosService } from '../../core/api/pos.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * What the promotions actually did.
 *
 * The figure this screen exists to put beside the others is what the shop makes when it is not
 * discounting. A promotion's margin on its own is a number nobody can interpret: twenty per cent
 * is either good or a disaster depending entirely on the ordinary margin, and without it in view
 * every campaign looks like a success or a catastrophe according to taste.
 */
@Component({
  selector: 'asap-promotion-uptake',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './promotion-uptake.html',
  styleUrl: './pos.scss',
})
export class PromotionUptakeReport implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly pos = inject(PosService);
  private readonly messages = inject(MessageService);

  protected readonly report = signal<PromotionUptake | null>(null);
  protected readonly loading = signal(true);

  protected from = '';
  protected to = '';

  protected readonly rows = computed(() => this.report()?.offers ?? []);

  /** What the shop makes when nothing is on offer, which is what every row is judged against. */
  protected readonly ordinaryMargin = computed(() => this.report()?.unpromotedMarginPercent ?? null);

  async ngOnInit(): Promise<void> {
    const today = new Date();
    const monthAgo = new Date(today);
    monthAgo.setMonth(monthAgo.getMonth() - 1);

    this.to = today.toISOString().slice(0, 10);
    this.from = monthAgo.toISOString().slice(0, 10);

    await this.load();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected percent(value: number | null | undefined): string {
    // Said rather than left blank. An empty cell in a column of percentages reads as zero, and
    // "we did not record the cost" is not zero margin.
    if (value === null || value === undefined) {
      return this.t('promotions.uptake.notKnown');
    }

    return `${new Intl.NumberFormat(this.i18n.locale(), { maximumFractionDigits: 2 }).format(value)}%`;
  }

  protected quantity(value: number): string {
    return new Intl.NumberFormat(this.i18n.locale(), { maximumFractionDigits: 5 }).format(value);
  }

  /**
   * Whether a campaign did worse than simply not discounting.
   *
   * Not the same as losing money. An offer can be perfectly profitable and still have been a bad
   * idea, and that is the comparison worth colouring.
   */
  protected isBelowOrdinary(margin: number | null): boolean {
    const ordinary = this.ordinaryMargin();

    // Two unknowns cannot be compared, and colouring one of them would be inventing a finding.
    return margin !== null && ordinary !== null && margin < ordinary;
  }

  protected async load(): Promise<void> {
    this.loading.set(true);

    try {
      this.report.set(await this.pos.promotionUptake(this.from, this.to));
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

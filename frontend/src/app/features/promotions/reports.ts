import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { OfferMovementRow, OfferUptakeRow } from '../../core/api/asap-api.models';
import { PromotionsService } from '../../core/api/promotions.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * What the offers actually did.
 *
 * The margin comes from what the costing engine said at the moment of sale — the same figure the
 * margin floor was checked against when the offer was let through — so the report and the refusal
 * can never disagree about the same offer.
 *
 * The second table is a comparison and nothing stronger. Sales move for a season, a competitor, the
 * weather, or a shelf that happened to be empty in the earlier window, and none of that is visible
 * here. Anything reporting a cannibalisation figure from two numbers would be inventing a cause out
 * of a coincidence, and it would be believed because it had a decimal point in it.
 */
@Component({
  selector: 'asap-promotion-reports',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './reports.html',
})
export class PromotionReports implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(PromotionsService);
  private readonly messages = inject(MessageService);

  protected readonly uptake = signal<OfferUptakeRow[]>([]);
  protected readonly movement = signal<OfferMovementRow[]>([]);
  protected readonly chosen = signal<string | null>(null);
  protected readonly loading = signal(true);

  protected from = '';
  protected to = '';

  async ngOnInit(): Promise<void> {
    const today = new Date();
    const start = new Date(today.getFullYear(), today.getMonth() - 3, today.getDate());

    this.to = today.toISOString().slice(0, 10);
    this.from = start.toISOString().slice(0, 10);

    try {
      await this.refresh();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected async refresh(): Promise<void> {
    try {
      this.uptake.set(await this.api.offerUptake(this.from, this.to));
    } catch (error) {
      this.messages.showError(error);
    }
  }

  protected async showMovement(row: OfferUptakeRow): Promise<void> {
    try {
      this.chosen.set(row.offerCode);
      this.movement.set(await this.api.offerMovement(row.offerCode));
    } catch (error) {
      this.messages.showError(error);
    }
  }

  protected describe(row: { name: string; nameArabic?: string | null }): string {
    return this.i18n.language() === 'ar' ? row.nameArabic || row.name : row.name;
  }

  protected describeItem(row: { description: string; descriptionArabic?: string | null }): string {
    return this.i18n.language() === 'ar'
      ? row.descriptionArabic || row.description
      : row.description;
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }
}

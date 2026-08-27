import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  Item,
  Offer,
  OfferKind,
  OfferPreview,
  OfferScope,
  SaveOfferRequest,
  StackingRule,
} from '../../core/api/asap-api.models';
import { InventoryService } from '../../core/api/inventory.service';
import { PromotionsService } from '../../core/api/promotions.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * Offers, and what one would do before it runs.
 *
 * The preview is the reason this screen exists. Somebody setting up "twenty per cent off
 * furniture" is choosing a percentage, not reading a cost sheet — and the pieces of furniture
 * that ruins are exactly the ones they will not think to check. Showing them here, at today's
 * costs, is the difference between a decision and a discovery at a till a fortnight later.
 */
@Component({
  selector: 'asap-offers',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './offers.html',
  styleUrl: './promotions.scss',
})
export class Offers implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly promotions = inject(PromotionsService);
  private readonly inventory = inject(InventoryService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly offers = signal<Offer[]>([]);
  protected readonly items = signal<Item[]>([]);
  protected readonly preview = signal<OfferPreview | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);

  protected code = '';
  protected name = '';
  protected nameArabic = '';
  protected kind: OfferKind = 'Percentage';
  protected scope: OfferScope = 'Everything';
  protected value: number | null = null;
  protected buyQuantity: number | null = null;
  protected getQuantity: number | null = null;
  protected startsOn = '';
  protected endsOn = '';
  protected stacking: StackingRule = 'Stacks';
  protected couponCode = '';
  protected targetItemNo = '';

  /** Whether the preview says anything would go below the floor. */
  protected readonly breaches = computed(() => this.preview()?.breaches ?? 0);

  /** The rows worth looking at first, which are the ones in trouble. */
  protected readonly rows = computed(() => this.preview()?.rows ?? []);

  async ngOnInit(): Promise<void> {
    this.startsOn = new Date().toISOString().slice(0, 10);

    try {
      const [offers, items] = await Promise.all([
        this.promotions.offers(),
        this.inventory.items(),
      ]);

      this.offers.set(offers);
      this.items.set(items.filter((item) => !item.isBlocked));
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canWrite(): boolean {
    return this.auth.can('Promotions.Offer.Create') || this.auth.can('Promotions.Offer.Update');
  }

  protected percent(value: number | null | undefined): string {
    if (value === null || value === undefined) {
      return '';
    }

    return `${new Intl.NumberFormat(this.i18n.locale(), { maximumFractionDigits: 2 }).format(value)}%`;
  }

  protected itemLabel(item: Item): string {
    const description =
      this.i18n.language() === 'ar' && item.descriptionArabic
        ? item.descriptionArabic
        : item.description;

    return `${item.no} — ${description}`;
  }

  /**
   * What a stacking rule is called.
   *
   * The key is built from the value, so the type has to be asserted. The alternative is three
   * near-identical branches that drift apart the first time a fourth rule is added.
   */
  protected stackingLabel(stacking: StackingRule): string {
    return this.t(`promotions.stacking.${stacking}` as TranslationKey);
  }

  protected offerName(offer: Offer): string {
    return this.i18n.language() === 'ar' && offer.nameArabic ? offer.nameArabic : offer.name;
  }

  /** What the offer says it does, in a sentence, for the list. */
  protected describe(offer: Offer): string {
    switch (offer.kind) {
      case 'Percentage':
        return this.t('promotions.describe.percentage', { Value: this.percent(offer.value) });

      case 'AmountPerUnit':
        return this.t('promotions.describe.amount', { Value: this.i18n.total(offer.value) });

      case 'BuyXGetY':
        return this.t('promotions.describe.buyGet', {
          Buy: offer.buyQuantity,
          Get: offer.getQuantity,
        });

      case 'Threshold':
        return this.t('promotions.describe.threshold', {
          Value: this.i18n.total(offer.value),
          Off: this.percent(offer.getDiscountPercent),
        });

      case 'FixedPrice':
        return this.t('promotions.describe.fixed', { Value: this.i18n.total(offer.value) });

      default:
        return '';
    }
  }

  protected isReady(): boolean {
    return !!this.code && !!this.name && !!this.startsOn;
  }

  /** What is on the form, as the server wants it. */
  private draft(): SaveOfferRequest {
    return {
      code: this.code,
      name: this.name,
      nameArabic: this.nameArabic || undefined,
      kind: this.kind,
      scope: this.scope,
      value: this.value ?? 0,
      buyQuantity: this.buyQuantity ?? 0,
      getQuantity: this.getQuantity ?? 0,
      getDiscountPercent: 100,
      startsOn: this.startsOn,
      endsOn: this.endsOn || undefined,
      stacking: this.stacking,
      couponCode: this.couponCode || undefined,
      targets:
        this.scope === 'Item' && this.targetItemNo ? [{ itemNo: this.targetItemNo }] : undefined,
    };
  }

  /** Asks what the offer would do, without committing to it. */
  protected async check(): Promise<void> {
    if (!this.isReady() || this.busy()) {
      return;
    }

    this.messages.clear();
    this.busy.set('preview');

    try {
      this.preview.set(await this.promotions.preview(this.draft()));
    } catch (error) {
      this.messages.showError(error, this.t('promotions.preview.action'));
    } finally {
      this.busy.set(null);
    }
  }

  protected async save(): Promise<void> {
    if (!this.isReady() || this.busy()) {
      return;
    }

    this.messages.clear();
    this.busy.set('save');

    try {
      const saved = await this.promotions.save(this.draft());

      this.messages.showAll(saved.messages ?? []);
      this.messages.showSuccess(this.t('promotions.offers.saved', { Code: saved.offer.code }));

      this.preview.set(null);
      this.code = '';
      this.name = '';
      this.nameArabic = '';
      this.value = null;
      this.targetItemNo = '';

      this.offers.set(await this.promotions.offers());
    } catch (error) {
      this.messages.showError(error, this.t('promotions.offers.save'));
    } finally {
      this.busy.set(null);
    }
  }
}

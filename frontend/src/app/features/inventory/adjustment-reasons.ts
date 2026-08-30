import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdjustmentReason, ShrinkageRow, StockLocation } from '../../core/api/asap-api.models';
import { InventoryService } from '../../core/api/inventory.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * What stock may be written off for, and what was written off under each.
 *
 * The report at the bottom is why the list exists. Breakage, theft and expiry have the same effect
 * on quantity and almost nothing else in common, and a single shrinkage figure covering all three
 * is a number nobody can act on.
 *
 * Adjustments made without a reason get a row of their own rather than being dropped. A report that
 * quietly omitted them would understate the total, and the gap between it and the ledger would be
 * exactly the entries nobody explained.
 */
@Component({
  selector: 'asap-adjustment-reasons',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './adjustment-reasons.html',
})
export class AdjustmentReasons implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(InventoryService);
  private readonly messages = inject(MessageService);

  protected readonly reasons = signal<AdjustmentReason[]>([]);
  protected readonly shrinkage = signal<ShrinkageRow[]>([]);
  protected readonly locations = signal<StockLocation[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);

  /** A new reason. */
  protected newCode = '';
  protected newName = '';
  protected newNameArabic = '';
  protected newAccount = '';
  protected newDirection: AdjustmentReason['direction'] = 'Either';
  protected newRequiresNote = false;

  /** The report's range. */
  protected from = '';
  protected to = '';
  protected location = '';

  async ngOnInit(): Promise<void> {
    const today = new Date();
    const start = new Date(today.getFullYear(), today.getMonth() - 1, today.getDate());

    this.to = today.toISOString().slice(0, 10);
    this.from = start.toISOString().slice(0, 10);

    try {
      const [reasons, locations] = await Promise.all([
        this.api.adjustmentReasons(true),
        this.api.locations(),
      ]);

      this.reasons.set(reasons);
      this.locations.set(locations);

      await this.report();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected async report(): Promise<void> {
    try {
      this.shrinkage.set(
        await this.api.shrinkage(this.from, this.to, this.location || undefined),
      );
    } catch (error) {
      this.shrinkage.set([]);
      this.messages.showError(error);
    }
  }

  protected async add(): Promise<void> {
    if (!this.newCode.trim()) {
      return;
    }

    this.busy.set(true);

    try {
      await this.api.saveAdjustmentReason({
        code: this.newCode.trim().toUpperCase(),
        name: this.newName.trim() || this.newCode.trim().toUpperCase(),
        nameArabic: this.newNameArabic.trim() || null,
        contraAccountNo: this.newAccount.trim() || null,
        direction: this.newDirection,
        requiresNote: this.newRequiresNote,
        isActive: true,
      });

      this.reasons.set(await this.api.adjustmentReasons(true));

      this.newCode = '';
      this.newName = '';
      this.newNameArabic = '';
      this.newAccount = '';
      this.newDirection = 'Either';
      this.newRequiresNote = false;
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async setActive(reason: AdjustmentReason, active: boolean): Promise<void> {
    this.busy.set(true);

    try {
      await this.api.saveAdjustmentReason({ ...reason, isActive: active });
      this.reasons.set(await this.api.adjustmentReasons(true));
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  /** What the whole period came to, unexplained adjustments included. */
  protected total(): number {
    return this.shrinkage().reduce((sum, row) => sum + row.costAmount, 0);
  }

  protected reasonName(reason: AdjustmentReason): string {
    return this.i18n.language() === 'ar' && reason.nameArabic ? reason.nameArabic : reason.name;
  }

  protected rowName(row: ShrinkageRow): string {
    return this.i18n.language() === 'ar' && row.reasonNameArabic
      ? row.reasonNameArabic
      : row.reasonName;
  }

  protected directionLabel(direction: string): string {
    if (direction === 'IncreaseOnly') {
      return this.t('inventory.reasons.increaseOnly');
    }

    return direction === 'DecreaseOnly'
      ? this.t('inventory.reasons.decreaseOnly')
      : this.t('inventory.reasons.either');
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }
}

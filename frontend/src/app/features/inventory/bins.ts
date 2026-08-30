import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Bin, BinContent, StockLocation } from '../../core/api/asap-api.models';
import { InventoryService } from '../../core/api/inventory.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * The shelves inside a location, and what is standing on them.
 *
 * A bin is a refinement of a location, never a substitute. Every stock figure and every valuation
 * stays per location; the bin only says where inside. Which is why the switch at the top is safe
 * to throw: turning bin tracking on cannot change what anything is worth.
 *
 * The contents table is the honest test of that. It is summed from the same ledger entries the
 * location total comes from, so the shelves add up to the location by construction — and anything
 * received before bins were turned on shows as the difference, which is exactly the stock somebody
 * still has to walk out and put away.
 */
@Component({
  selector: 'asap-bins',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './bins.html',
})
export class Bins implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(InventoryService);
  private readonly messages = inject(MessageService);

  protected readonly locations = signal<StockLocation[]>([]);
  protected readonly bins = signal<Bin[]>([]);
  protected readonly contents = signal<BinContent[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);

  protected readonly selected = signal<StockLocation | null>(null);

  /** A new shelf. */
  protected newCode = '';
  protected newName = '';
  protected newNameArabic = '';
  protected newPickOrder = 0;
  protected newIsReceiving = false;

  async ngOnInit(): Promise<void> {
    try {
      const locations = await this.api.locations();

      this.locations.set(locations);

      if (locations.length > 0) {
        await this.choose(locations[0].code);
      }
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected async choose(locationCode: string): Promise<void> {
    const location = this.locations().find((l) => l.code === locationCode) ?? null;

    this.selected.set(location);
    this.newCode = '';
    this.newName = '';
    this.newNameArabic = '';
    this.newPickOrder = 0;
    this.newIsReceiving = false;

    if (!location) {
      this.bins.set([]);
      this.contents.set([]);
      return;
    }

    await this.refresh(location.code);
  }

  protected async onLocationChange(event: Event): Promise<void> {
    await this.choose((event.target as HTMLSelectElement).value);
  }

  protected async toggleTracking(): Promise<void> {
    const location = this.selected();

    if (!location) {
      return;
    }

    this.busy.set(true);

    try {
      const saved = await this.api.setBinTracking(location.code, !location.usesBins);

      this.locations.set(
        this.locations().map((l) => (l.code === saved.code ? { ...l, usesBins: saved.usesBins } : l)),
      );

      this.selected.set({ ...location, usesBins: saved.usesBins });

      await this.refresh(location.code);
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async add(): Promise<void> {
    const location = this.selected();

    if (!location || !this.newCode.trim()) {
      return;
    }

    this.busy.set(true);

    try {
      await this.api.saveBin(location.code, {
        code: this.newCode.trim().toUpperCase(),
        name: this.newName.trim() || null,
        nameArabic: this.newNameArabic.trim() || null,
        isReceiving: this.newIsReceiving,
        pickOrder: Number(this.newPickOrder) || 0,
        isBlocked: false,
      });

      this.newCode = '';
      this.newName = '';
      this.newNameArabic = '';
      this.newPickOrder = 0;
      this.newIsReceiving = false;

      await this.refresh(location.code);
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async setBlocked(bin: Bin, blocked: boolean): Promise<void> {
    await this.save({ ...bin, isBlocked: blocked });
  }

  protected async setReceiving(bin: Bin): Promise<void> {
    await this.save({ ...bin, isReceiving: true });
  }

  protected async remove(bin: Bin): Promise<void> {
    const location = this.selected();

    if (!location) {
      return;
    }

    this.busy.set(true);

    try {
      await this.api.removeBin(location.code, bin.code);
      await this.refresh(location.code);
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  /** What is standing on the shelves, added up. */
  protected onShelves(): number {
    return this.contents().reduce((total, row) => total + row.quantity, 0);
  }

  protected binName(bin: Bin): string {
    return this.i18n.language() === 'ar' && bin.nameArabic ? bin.nameArabic : (bin.name ?? '');
  }

  protected itemName(row: BinContent): string {
    return this.i18n.language() === 'ar' && row.descriptionArabic
      ? row.descriptionArabic
      : row.description;
  }

  protected locationName(location: StockLocation): string {
    return this.i18n.language() === 'ar' && location.nameArabic
      ? location.nameArabic
      : location.name;
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  private async save(bin: Bin): Promise<void> {
    const location = this.selected();

    if (!location) {
      return;
    }

    this.busy.set(true);

    try {
      await this.api.saveBin(location.code, bin);
      await this.refresh(location.code);
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  private async refresh(locationCode: string): Promise<void> {
    try {
      const [bins, contents] = await Promise.all([
        this.api.bins(locationCode),
        this.api.binContents(locationCode),
      ]);

      this.bins.set(bins);
      this.contents.set(contents);
    } catch (error) {
      this.bins.set([]);
      this.contents.set([]);
      this.messages.showError(error);
    }
  }
}

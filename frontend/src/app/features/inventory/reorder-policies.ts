import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  Item,
  StockLocation,
  Party,
  ReorderPolicyRow,
} from '../../core/api/asap-api.models';
import { AuthService } from '../../core/auth/auth.service';
import { FinanceService } from '../../core/api/finance.service';
import { InventoryService } from '../../core/api/inventory.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * When each place reorders each item, and how much.
 *
 * The kind of policy changes which figures matter, so the form shows one pair or the other rather
 * than four boxes of which two are always ignored. A form that asks for a maximum on a
 * fixed-quantity policy invites somebody to fill it in and wonder why nothing uses it.
 */
@Component({
  selector: 'asap-reorder-policies',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './reorder-policies.html',
})
export class ReorderPolicies implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(InventoryService);
  private readonly finance = inject(FinanceService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly policies = signal<ReorderPolicyRow[]>([]);
  protected readonly items = signal<Item[]>([]);
  protected readonly locations = signal<StockLocation[]>([]);
  protected readonly vendors = signal<Party[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly editing = signal<ReorderPolicyRow | null>(null);

  protected filterLocation = '';

  protected draftItemNo = '';
  protected draftLocationCode = '';
  protected draftKind: 'FixedQuantity' | 'UpToMaximum' = 'FixedQuantity';
  protected draftReorderPoint: number | null = null;
  protected draftReorderQuantity: number | null = null;
  protected draftMaximumInventory: number | null = null;
  protected draftMinimumOrderQuantity: number | null = null;
  protected draftOrderMultiple: number | null = null;
  protected draftLeadTimeDays: number | null = null;
  protected draftVendorNo = '';
  protected draftActive = true;

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canWrite(): boolean {
    return this.auth.can('Inventory.Item.Update');
  }

  /** What the policy asks for, in words, so the list is readable without opening each row. */
  protected rule(policy: ReorderPolicyRow): string {
    return policy.kind === 'UpToMaximum'
      ? this.t('inventory.reorder.ruleUpTo', {
          Point: policy.reorderPoint,
          Maximum: policy.maximumInventory,
        })
      : this.t('inventory.reorder.ruleFixed', {
          Point: policy.reorderPoint,
          Quantity: policy.reorderQuantity,
        });
  }

  protected startNew(): void {
    this.editing.set(null);
    this.draftItemNo = '';
    this.draftLocationCode = this.filterLocation;
    this.draftKind = 'FixedQuantity';
    this.draftReorderPoint = null;
    this.draftReorderQuantity = null;
    this.draftMaximumInventory = null;
    this.draftMinimumOrderQuantity = null;
    this.draftOrderMultiple = null;
    this.draftLeadTimeDays = null;
    this.draftVendorNo = '';
    this.draftActive = true;
  }

  protected select(policy: ReorderPolicyRow): void {
    this.editing.set(policy);
    this.draftItemNo = policy.itemNo;
    this.draftLocationCode = policy.locationCode;
    this.draftKind = policy.kind;
    this.draftReorderPoint = policy.reorderPoint;
    this.draftReorderQuantity = policy.reorderQuantity || null;
    this.draftMaximumInventory = policy.maximumInventory || null;
    this.draftMinimumOrderQuantity = policy.minimumOrderQuantity || null;
    this.draftOrderMultiple = policy.orderMultiple || null;
    this.draftLeadTimeDays = policy.leadTimeDays || null;
    this.draftVendorNo = policy.vendorNo ?? '';
    this.draftActive = policy.isActive;
  }

  protected async save(): Promise<void> {
    if (!this.draftItemNo || !this.draftLocationCode) {
      return;
    }

    this.busy.set(true);

    try {
      await this.api.saveReorderPolicy({
        itemNo: this.draftItemNo,
        locationCode: this.draftLocationCode,
        kind: this.draftKind,
        reorderPoint: this.draftReorderPoint ?? 0,
        reorderQuantity: this.draftReorderQuantity ?? 0,
        maximumInventory: this.draftMaximumInventory ?? 0,
        minimumOrderQuantity: this.draftMinimumOrderQuantity ?? 0,
        orderMultiple: this.draftOrderMultiple ?? 0,
        leadTimeDays: this.draftLeadTimeDays ?? 0,
        vendorNo: this.draftVendorNo || null,
        isActive: this.draftActive,
      });

      this.messages.showSuccess(
        this.t('inventory.reorder.saved', {
          ItemNo: this.draftItemNo,
          LocationCode: this.draftLocationCode,
        }),
      );

      await this.reload();
      this.startNew();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(policy: ReorderPolicyRow): Promise<void> {
    this.busy.set(true);

    try {
      await this.api.removeReorderPolicy(policy.itemNo, policy.locationCode);

      this.messages.showSuccess(
        this.t('inventory.reorder.removed', {
          ItemNo: policy.itemNo,
          LocationCode: policy.locationCode,
        }),
      );

      await this.reload();

      if (this.editing()?.itemNo === policy.itemNo) {
        this.startNew();
      }
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async filter(): Promise<void> {
    await this.reload();
  }

  private async reload(): Promise<void> {
    this.loading.set(true);

    try {
      const [policies, items, locations, vendors] = await Promise.all([
        this.api.reorderPolicies(this.filterLocation || undefined),
        this.items().length > 0 ? Promise.resolve(this.items()) : this.api.items(),
        this.locations().length > 0 ? Promise.resolve(this.locations()) : this.api.locations(),
        this.vendors().length > 0 ? Promise.resolve(this.vendors()) : this.finance.parties('Vendor'),
      ]);

      this.policies.set(policies);
      this.items.set(items.filter((item) => !item.isBlocked));
      this.locations.set(locations);
      this.vendors.set(vendors);
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

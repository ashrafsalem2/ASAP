import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  CustomerGroup,
  CustomerGroupPriceList,
  Party,
  PriceList,
} from '../../core/api/asap-api.models';
import { FinanceService } from '../../core/api/finance.service';
import { SalesService } from '../../core/api/sales.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * The kinds of customer, who is in each, and what each kind pays.
 *
 * Three things sit on one screen because they are useless apart. A group with no members and no
 * price list is a word; the reason to create one is always either the price a class of customer
 * gets or the offer they are entitled to, and both are decided here.
 *
 * The member count is on the list for the same reason a rate is on the currency list: a group
 * nobody is in is the one somebody is about to withdraw by mistake, and a group everybody is in
 * is the one to be careful with.
 */
@Component({
  selector: 'asap-customer-groups',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './customer-groups.html',
  styleUrl: './finance.scss',
})
export class CustomerGroups implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly finance = inject(FinanceService);
  private readonly sales = inject(SalesService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly groups = signal<CustomerGroup[]>([]);
  protected readonly customers = signal<Party[]>([]);
  protected readonly priceLists = signal<PriceList[]>([]);
  protected readonly groupLists = signal<CustomerGroupPriceList[]>([]);
  protected readonly selected = signal<CustomerGroup | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);

  protected draftCode = '';
  protected draftName = '';
  protected draftNameArabic = '';
  protected draftDescription = '';
  protected draftActive = true;
  protected joiningCustomerNo = '';

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canWrite(): boolean {
    return this.auth.can('Finance.Party.Update');
  }

  protected canPrice(): boolean {
    return this.auth.can('Sales.PriceList.Update');
  }

  protected name(group: CustomerGroup): string {
    return this.i18n.language() === 'ar' && group.nameArabic ? group.nameArabic : group.name;
  }

  /** The list a group is on, or an empty string where it is on none. */
  protected listOf(code: string): string {
    return this.groupLists().find((g) => g.customerGroupCode === code)?.priceListCode ?? '';
  }

  /** Who is in the selected group. */
  protected members(): Party[] {
    const group = this.selected();

    return group ? this.customers().filter((c) => c.customerGroupCode === group.code) : [];
  }

  /** Customers in no group at all, which is who can be added without taking them off anything. */
  protected unassigned(): Party[] {
    return this.customers().filter((c) => !c.customerGroupCode);
  }

  protected select(group: CustomerGroup): void {
    this.selected.set(group);
    this.joiningCustomerNo = '';

    this.draftCode = group.code;
    this.draftName = group.name;
    this.draftNameArabic = group.nameArabic ?? '';
    this.draftDescription = group.description ?? '';
    this.draftActive = group.isActive;
  }

  protected startNew(): void {
    this.selected.set(null);
    this.draftCode = '';
    this.draftName = '';
    this.draftNameArabic = '';
    this.draftDescription = '';
    this.draftActive = true;
  }

  protected async save(): Promise<void> {
    if (!this.draftCode.trim() || !this.draftName.trim()) {
      return;
    }

    this.busy.set(true);

    try {
      await this.finance.saveCustomerGroup({
        code: this.draftCode.trim().toUpperCase(),
        name: this.draftName.trim(),
        nameArabic: this.draftNameArabic.trim() || null,
        description: this.draftDescription.trim() || null,
        isActive: this.draftActive,
      });

      this.messages.showSuccess(this.t('finance.customerGroups.saved', { code: this.draftCode }));

      const code = this.draftCode.trim().toUpperCase();

      await this.reload();

      const saved = this.groups().find((g) => g.code === code);

      if (saved) {
        this.select(saved);
      }
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async join(): Promise<void> {
    const group = this.selected();

    if (!group || !this.joiningCustomerNo) {
      return;
    }

    await this.assign(this.joiningCustomerNo, group.code);
    this.joiningCustomerNo = '';
  }

  protected async leave(customerNo: string): Promise<void> {
    await this.assign(customerNo, null);
  }

  /** Puts a whole group on a price list, or takes it off one. */
  protected async setList(code: string, priceListCode: string): Promise<void> {
    this.busy.set(true);

    try {
      await this.sales.assignGroupPriceList(code, priceListCode || null);
      this.groupLists.set(await this.sales.groupPriceListAssignments());

      this.messages.showSuccess(this.t('finance.customerGroups.listSet', { code }));
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  private async assign(customerNo: string, groupCode: string | null): Promise<void> {
    this.busy.set(true);

    try {
      await this.finance.assignCustomerGroup(customerNo, groupCode);

      this.messages.showSuccess(
        groupCode
          ? this.t('finance.customerGroups.joined', { customerNo, code: groupCode })
          : this.t('finance.customerGroups.left', { customerNo }),
      );

      const code = this.selected()?.code;

      await this.reload();

      const still = this.groups().find((g) => g.code === code);

      if (still) {
        this.selected.set(still);
      }
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  private async reload(): Promise<void> {
    this.loading.set(true);

    try {
      const [groups, customers] = await Promise.all([
        this.finance.customerGroups(),
        this.finance.parties('Customer'),
      ]);

      this.groups.set(groups);
      this.customers.set(customers);

      // Price lists are a sales permission, and somebody may maintain groups without holding it.
      // The screen still works without them; it simply does not offer to set a price.
      if (this.canPrice()) {
        const [lists, assignments] = await Promise.all([
          this.sales.priceLists(),
          this.sales.groupPriceListAssignments(),
        ]);

        this.priceLists.set(lists);
        this.groupLists.set(assignments);
      }
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

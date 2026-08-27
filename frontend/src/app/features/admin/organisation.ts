import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Branch, Companies, Company } from '../../core/api/asap-api.models';
import { OrganisationService } from '../../core/api/organisation.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/** Which of the two lists this screen is showing. */
export type OrganisationKind = 'companies' | 'branches';

/**
 * Companies and branches.
 *
 * One screen for both because they are the same shape and the same job, and because a company
 * with no branches cannot trade: opening the first one is part of setting the company up rather
 * than a separate errand.
 *
 * A branch that has closed is made inactive, never removed. Last year's documents point at it and
 * a branch report for last year has to be able to name it.
 */
@Component({
  selector: 'asap-organisation',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './organisation.html',
  styleUrl: './admin.scss',
})
export class Organisation implements OnInit {
  /** Set by the route, because the menu declares the two as separate pages. */
  readonly kind = input<OrganisationKind>('branches');

  protected readonly i18n = inject(I18nService);
  private readonly organisation = inject(OrganisationService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly companies = signal<Companies | null>(null);
  protected readonly branches = signal<Branch[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly editing = signal<string | null>(null);

  protected readonly branchKinds = ['HeadOffice', 'Store', 'Warehouse', 'Office'] as const;

  protected code = '';
  protected name = '';
  protected nameArabic = '';
  protected isActive = true;

  // Company only.
  protected baseCurrencyCode = 'SAR';
  protected registrationNo = '';
  protected taxRegistrationNo = '';
  protected fiscalYearStartMonth = 1;
  protected settled = false;

  // Branch only.
  protected branchKind: (typeof this.branchKinds)[number] = 'Store';
  protected city = '';
  protected address = '';
  protected phone = '';

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canWrite(): boolean {
    return this.kind() === 'companies'
      ? this.auth.can('Platform.Company.Create') || this.auth.can('Platform.Company.Update')
      : this.auth.can('Platform.Branch.Create') || this.auth.can('Platform.Branch.Update');
  }

  protected kindLabel(value: string): string {
    return this.t(`admin.org.kind.${value}` as TranslationKey);
  }

  protected startNew(): void {
    this.editing.set(null);
    this.code = '';
    this.name = '';
    this.nameArabic = '';
    this.isActive = true;
    this.settled = false;
    this.registrationNo = '';
    this.taxRegistrationNo = '';
    this.city = '';
    this.address = '';
    this.phone = '';
  }

  protected editCompany(company: Company): void {
    this.editing.set(company.code);
    this.code = company.code;
    this.name = company.name;
    this.nameArabic = company.nameArabic ?? '';
    this.isActive = company.isActive ?? true;
    this.baseCurrencyCode = company.baseCurrencyCode;
    this.registrationNo = company.registrationNo ?? '';
    this.taxRegistrationNo = company.taxRegistrationNo ?? '';
    this.fiscalYearStartMonth = company.fiscalYearStartMonth ?? 1;

    // Once anything has been posted, the currency and the year's opening month describe how the
    // existing figures were measured. The server refuses to change them; the screen says so.
    this.settled = company.hasPostedEntries === true;
  }

  protected editBranch(branch: Branch): void {
    this.editing.set(branch.code);
    this.code = branch.code;
    this.name = branch.name;
    this.nameArabic = branch.nameArabic ?? '';
    this.isActive = branch.isActive;
    this.branchKind = branch.kind as (typeof this.branchKinds)[number];
    this.city = branch.city ?? '';
    this.address = branch.address ?? '';
    this.phone = branch.phone ?? '';
  }

  protected async save(): Promise<void> {
    if (!this.code.trim() || !this.name.trim()) {
      return;
    }

    this.busy.set(true);

    try {
      if (this.kind() === 'companies') {
        await this.organisation.saveCompany({
          code: this.code.trim(),
          name: this.name.trim(),
          nameArabic: this.nameArabic.trim() || null,
          baseCurrencyCode: this.baseCurrencyCode.trim() || 'SAR',
          registrationNo: this.registrationNo.trim() || null,
          taxRegistrationNo: this.taxRegistrationNo.trim() || null,
          fiscalYearStartMonth: this.fiscalYearStartMonth,
          isActive: this.isActive,
        });
      } else {
        await this.organisation.saveBranch({
          code: this.code.trim(),
          name: this.name.trim(),
          nameArabic: this.nameArabic.trim() || null,
          kind: this.branchKind,
          city: this.city.trim() || null,
          address: this.address.trim() || null,
          phone: this.phone.trim() || null,
          isActive: this.isActive,
        });
      }

      this.messages.showSuccess(this.t('admin.org.saved', { code: this.code }));
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
      if (this.kind() === 'companies') {
        this.companies.set(await this.organisation.companies());
      } else {
        // Inactive ones included, because this is the screen where somebody reopens one.
        this.branches.set(await this.organisation.branches(true));
      }
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

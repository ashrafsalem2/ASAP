import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ContractKind, Employee, EmploymentContractRow } from '../../core/api/asap-api.models';
import { AuthService } from '../../core/auth/auth.service';
import { HrService } from '../../core/api/hr.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * What people have been engaged on, and when it changed.
 *
 * The list is per person and in date order, because the useful question is never "what is the
 * wage" but "what was the wage then, and what changed it". A screen that showed only the current
 * contract would be the employee card again, which is the thing contracts exist to fix.
 *
 * Superseding is the default when somebody already has a contract, because a raise entered as a
 * fresh contract is a raise that overlaps the old one and is refused.
 */
@Component({
  selector: 'asap-employment-contracts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './contracts.html',
})
export class EmploymentContracts implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(HrService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly contracts = signal<EmploymentContractRow[]>([]);
  protected readonly employees = signal<Employee[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);

  protected selectedEmployeeNo = '';

  protected draftStartsOn = '';
  protected draftEndsOn = '';
  protected draftKind: ContractKind = 'Permanent';
  protected draftBasicWage: number | null = null;
  protected draftAllowances: number | null = null;
  protected draftReference = '';
  protected draftSignedOn = '';
  protected draftReason = '';

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canWrite(): boolean {
    return this.auth.can('Hr.Employee.Update');
  }

  /** A fixed term or a probation must have an end; a permanent contract must not. */
  protected needsEnd(): boolean {
    return this.draftKind !== 'Permanent';
  }

  /** Whether recording this would close an existing contract rather than sit beside one. */
  protected wouldSupersede(): boolean {
    return this.contracts().some((c) => c.endsOn === null);
  }

  protected nameOf(employeeNo: string): string {
    return this.employees().find((e) => e.no === employeeNo)?.name ?? employeeNo;
  }

  protected kindLabel(kind: ContractKind): string {
    return kind === 'FixedTerm'
      ? this.t('hr.contracts.fixedTerm')
      : kind === 'Probation'
        ? this.t('hr.contracts.probation')
        : this.t('hr.contracts.permanent');
  }

  /** Whether this is the contract in force today. */
  protected isCurrent(contract: EmploymentContractRow): boolean {
    const today = new Date().toISOString().slice(0, 10);

    return contract.startsOn <= today && (contract.endsOn === null || contract.endsOn >= today);
  }

  protected async select(): Promise<void> {
    await this.reload();
  }

  protected async record(): Promise<void> {
    if (!this.selectedEmployeeNo || !this.draftStartsOn || !this.draftBasicWage) {
      return;
    }

    this.busy.set(true);

    try {
      await this.api.recordContract(this.selectedEmployeeNo, {
        startsOn: this.draftStartsOn,
        endsOn: this.draftEndsOn || null,
        kind: this.draftKind,
        basicWage: this.draftBasicWage,
        allowances: this.draftAllowances ?? 0,
        reference: this.draftReference || null,
        signedOn: this.draftSignedOn || null,
        reason: this.draftReason || null,

        // A new contract beside an open one always overlaps it. Superseding closes the old one
        // the day before, which is what a raise or a renewal actually is.
        supersede: this.wouldSupersede(),
      });

      this.messages.showSuccess(
        this.t('hr.contracts.recorded', {
          EmployeeNo: this.selectedEmployeeNo,
          StartsOn: this.draftStartsOn,
        }),
      );

      this.clearDraft();
      await this.reload();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  private clearDraft(): void {
    this.draftStartsOn = '';
    this.draftEndsOn = '';
    this.draftKind = 'Permanent';
    this.draftBasicWage = null;
    this.draftAllowances = null;
    this.draftReference = '';
    this.draftSignedOn = '';
    this.draftReason = '';
  }

  private async reload(): Promise<void> {
    this.loading.set(true);

    try {
      const [contracts, employees] = await Promise.all([
        this.api.contracts(this.selectedEmployeeNo || undefined),
        this.employees().length > 0 ? Promise.resolve(this.employees()) : this.api.employees(),
      ]);

      this.contracts.set(contracts);
      this.employees.set(employees);
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

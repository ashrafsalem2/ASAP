import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Branch, Employee, LeavingReason } from '../../core/api/asap-api.models';
import { HrService } from '../../core/api/hr.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * The staff list, and the branch history that decides who pays for them.
 *
 * The history is what this screen is for. A name and a wage would fit on a form; where somebody
 * has worked and when is what splits the wage between branches at the end of the month, and a
 * transfer nobody recorded moves real money to the wrong shop — quietly, and correctly totalled,
 * so nothing ever looks wrong.
 */
@Component({
  selector: 'asap-employees',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './employees.html',
  styleUrl: './hr.scss',
})
export class Employees implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly hr = inject(HrService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly employees = signal<Employee[]>([]);
  protected readonly branches = signal<Branch[]>([]);
  protected readonly selected = signal<Employee | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);
  protected readonly includeLeavers = signal(false);

  protected readonly leavingReasons: readonly LeavingReason[] = [
    'Resignation',
    'Termination',
    'EndOfContract',
    'Retirement',
    'Death',
    'Disability',
  ];

  protected hireName = '';
  protected hireNameArabic = '';
  protected hireNo = '';
  protected hireNationalId = '';
  protected hireNationality = '';
  protected hireOn = '';
  protected hireBranchId = '';
  protected hireBasicWage: number | null = null;
  protected hireAllowances: number | null = null;

  protected transferBranchId = '';
  protected transferFrom = '';
  protected transferReason = '';

  protected leftOn = '';
  protected leavingReason: LeavingReason = 'Resignation';

  /** Whether anything about pay may be shown at all. */
  protected readonly showWages = computed(() => this.auth.can('Hr.Wage.Read'));

  async ngOnInit(): Promise<void> {
    this.hireOn = this.today();
    this.transferFrom = this.today();
    this.leftOn = this.today();

    await this.reload();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canHire(): boolean {
    return this.auth.can('Hr.Employee.Create');
  }

  protected canUpdate(): boolean {
    return this.auth.can('Hr.Employee.Update');
  }

  /** The branch somebody is at today, named rather than keyed. */
  protected branchNow(employee: Employee): string {
    const today = this.today();

    const current = employee.branchAssignments.find(
      (a) => a.fromDate <= today && (a.toDate === null || a.toDate >= today),
    );

    return current ? this.branchName(current.branchId) : '';
  }

  /**
   * A branch by its key.
   *
   * Falls back to the key rather than to a blank. A row that quietly loses its branch is worse
   * than an ugly one: the reader has no way to tell it apart from a row that never had one.
   */
  protected branchName(branchId: string): string {
    const branch = this.branches().find((b) => b.id === branchId);

    if (!branch) {
      return branchId;
    }

    return this.i18n.language() === 'ar' && branch.nameArabic ? branch.nameArabic : branch.name;
  }

  protected statusLabel(status: string): string {
    return this.t(`hr.status.${status}` as TranslationKey);
  }

  protected leavingLabel(reason: string): string {
    return this.t(`hr.leaving.${reason}` as TranslationKey);
  }

  protected async select(employee: Employee): Promise<void> {
    try {
      this.selected.set(await this.hr.employee(employee.no));
    } catch (error) {
      this.messages.showError(error);
    }
  }

  protected async toggleLeavers(): Promise<void> {
    this.includeLeavers.update((on) => !on);
    await this.reload();
  }

  protected async hire(): Promise<void> {
    if (!this.hireName.trim() || !this.hireOn) {
      return;
    }

    this.busy.set('hire');

    try {
      const saved = await this.hr.hire({
        name: this.hireName.trim(),
        nameArabic: this.hireNameArabic.trim() || null,
        no: this.hireNo.trim() || null,
        nationalId: this.hireNationalId.trim() || null,
        nationality: this.hireNationality.trim() || null,
        hiredOn: this.hireOn,
        branchId: this.hireBranchId || null,
        basicWage: this.hireBasicWage ?? 0,
        allowances: this.hireAllowances ?? 0,
      });

      this.messages.showAll(saved.messages);
      this.messages.showSuccess(
        this.t('hr.employees.hired', { name: saved.employee.name, no: saved.employee.no }),
      );

      this.hireName = '';
      this.hireNameArabic = '';
      this.hireNo = '';
      this.hireNationalId = '';
      this.hireBasicWage = null;
      this.hireAllowances = null;

      await this.reload();
      this.selected.set(saved.employee);
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(null);
    }
  }

  protected async transfer(): Promise<void> {
    const employee = this.selected();

    if (!employee || !this.transferBranchId || !this.transferFrom) {
      return;
    }

    this.busy.set('transfer');

    try {
      const saved = await this.hr.transfer(employee.no, {
        branchId: this.transferBranchId,
        fromDate: this.transferFrom,
        reason: this.transferReason.trim() || null,
      });

      this.messages.showAll(saved.messages);
      this.messages.showSuccess(
        this.t('hr.employees.transferred', { no: employee.no, from: this.transferFrom }),
      );

      this.transferReason = '';
      this.selected.set(saved.employee);
      await this.reload();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(null);
    }
  }

  protected async recordLeaving(): Promise<void> {
    const employee = this.selected();

    if (!employee || !this.leftOn) {
      return;
    }

    this.busy.set('leaving');

    try {
      const saved = await this.hr.recordLeaving(employee.no, {
        leftOn: this.leftOn,
        reason: this.leavingReason,
      });

      this.messages.showAll(saved.messages);
      this.messages.showSuccess(this.t('hr.employees.left', { no: employee.no }));

      this.selected.set(saved.employee);
      await this.reload();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(null);
    }
  }

  private async reload(): Promise<void> {
    this.loading.set(true);

    try {
      const [employees, branches] = await Promise.all([
        this.hr.employees(this.includeLeavers()),
        this.hr.branches(),
      ]);

      this.employees.set(employees);
      this.branches.set(branches);

      if (!this.hireBranchId && branches.length > 0) {
        this.hireBranchId = branches[0].id;
      }
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }
}

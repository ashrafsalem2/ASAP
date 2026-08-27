import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  Employee,
  LeaveEntitlement,
  LeaveKind,
  LeaveRequest,
} from '../../core/api/asap-api.models';
import { HrService } from '../../core/api/hr.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * The leave register: who asked to be away, what was decided, and what is left.
 *
 * The balance is why this screen matters more than a calendar would. Leave earned was always
 * easy; leave remaining needs a record of what was taken, and without one the company's leave
 * liability was everything anybody had ever accrued — a number that is wrong in the company's
 * favour every year until somebody leaves and asks for what they are owed.
 */
@Component({
  selector: 'asap-leave',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './leave.html',
  styleUrl: './hr.scss',
})
export class Leave implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly hr = inject(HrService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly requests = signal<LeaveRequest[]>([]);
  protected readonly employees = signal<Employee[]>([]);
  protected readonly balance = signal<LeaveEntitlement | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);

  protected readonly kinds: readonly LeaveKind[] = [
    'Annual',
    'Sick',
    'Unpaid',
    'Maternity',
    'Hajj',
    'Marriage',
    'Bereavement',
    'Examination',
  ];

  protected employeeNo = '';
  protected kind: LeaveKind = 'Annual';
  protected fromDate = '';
  protected toDate = '';
  protected reason = '';

  async ngOnInit(): Promise<void> {
    this.fromDate = this.today();
    this.toDate = this.today();

    await this.reload();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canAsk(): boolean {
    return this.auth.can('Hr.Leave.Create');
  }

  protected canDecide(): boolean {
    return this.auth.can('Hr.Leave.Approve');
  }

  protected kindLabel(kind: string): string {
    return this.t(`hr.leaveKind.${kind}` as TranslationKey);
  }

  protected statusLabel(status: string): string {
    return this.t(`hr.leaveStatus.${status}` as TranslationKey);
  }

  /** How many days a draft would cover, so somebody sees it before they ask. */
  protected draftDays(): number {
    if (!this.fromDate || !this.toDate || this.toDate < this.fromDate) {
      return 0;
    }

    const from = Date.parse(`${this.fromDate}T00:00:00Z`);
    const to = Date.parse(`${this.toDate}T00:00:00Z`);

    return Math.round((to - from) / 86_400_000) + 1;
  }

  /**
   * Reads the balance as it will stand on the last day being asked for.
   *
   * Not as it stands today. Somebody booking October wants to know what they will have in
   * October: today's balance ignores both what they will accrue between now and then and the
   * leave they have already been granted for the days in between.
   */
  protected async selectEmployee(): Promise<void> {
    this.balance.set(null);

    if (!this.employeeNo) {
      return;
    }

    try {
      this.balance.set(await this.hr.leaveBalance(this.employeeNo, this.toDate || undefined));
    } catch (error) {
      this.messages.showError(error);
    }
  }

  protected async ask(): Promise<void> {
    if (!this.employeeNo || !this.fromDate || !this.toDate) {
      return;
    }

    this.busy.set('ask');

    try {
      const saved = await this.hr.requestLeave({
        employeeNo: this.employeeNo,
        kind: this.kind,
        fromDate: this.fromDate,
        toDate: this.toDate,
        reason: this.reason.trim() || null,
      });

      this.messages.showAll(saved.messages);
      this.messages.showSuccess(
        this.t('hr.leave.asked', { no: saved.request.no, days: saved.request.days }),
      );

      this.reason = '';
      await this.reload();
      await this.selectEmployee();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(null);
    }
  }

  protected async decide(
    request: LeaveRequest,
    decision: 'approve' | 'reject' | 'cancel',
  ): Promise<void> {
    this.busy.set(request.no);

    try {
      const decided = await this.hr.decideLeave(request.no, decision);

      this.messages.showSuccess(
        this.t('hr.leave.decided', {
          no: decided.no,
          status: this.statusLabel(decided.status),
        }),
      );

      await this.reload();
      await this.selectEmployee();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(null);
    }
  }

  private async reload(): Promise<void> {
    this.loading.set(true);

    try {
      const [requests, employees] = await Promise.all([
        this.hr.leaveRequests(),
        this.hr.employees(),
      ]);

      this.requests.set(requests);
      this.employees.set(employees);

      if (!this.employeeNo && employees.length > 0) {
        this.employeeNo = employees[0].no;
        await this.selectEmployee();
      }
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  private today(): string {
    const now = new Date();
    const month = `${now.getMonth() + 1}`.padStart(2, '0');
    const day = `${now.getDate()}`.padStart(2, '0');

    return `${now.getFullYear()}-${month}-${day}`;
  }
}

import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  AttendanceRow,
  Employee,
  ShiftAssignmentRow,
  ShiftRow,
} from '../../core/api/asap-api.models';
import { AuthService } from '../../core/auth/auth.service';
import { HrService } from '../../core/api/hr.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * What people did, and what was expected of them.
 *
 * The four measured figures are all shown and none is netted against another. Somebody twenty
 * minutes late who stayed an hour is late and has overtime, and a single combined number would
 * answer neither the manager's question nor the payroll clerk's.
 *
 * Nothing here deducts anything. That is deliberate and the help says so: a clock is a fact and a
 * deduction is a decision, and the first anybody knew of a rule that conflated them would be a
 * short payslip.
 */
@Component({
  selector: 'asap-attendance',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './attendance.html',
})
export class Attendance implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(HrService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly rows = signal<AttendanceRow[]>([]);
  protected readonly shifts = signal<ShiftRow[]>([]);
  protected readonly assignments = signal<ShiftAssignmentRow[]>([]);
  protected readonly employees = signal<Employee[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);

  protected filterEmployeeNo = '';
  protected filterFrom = '';
  protected filterTo = '';

  protected draftEmployeeNo = '';
  protected draftDate = '';
  protected draftIn = '';
  protected draftOut = '';
  protected draftNote = '';

  protected assignEmployeeNo = '';
  protected assignShiftCode = '';
  protected assignFrom = '';

  async ngOnInit(): Promise<void> {
    const today = new Date();
    const start = new Date(today);

    start.setDate(start.getDate() - 13);

    this.filterTo = today.toISOString().slice(0, 10);
    this.filterFrom = start.toISOString().slice(0, 10);
    this.draftDate = this.filterTo;
    this.assignFrom = this.filterTo;

    await this.reload();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canWrite(): boolean {
    return this.auth.can('Hr.Employee.Update');
  }

  /** Minutes as hours and minutes, because nobody reads 487. */
  protected hours(minutes: number): string {
    if (minutes === 0) {
      return '—';
    }

    const h = Math.floor(minutes / 60);
    const m = minutes % 60;

    return h > 0 ? `${h}h ${m.toString().padStart(2, '0')}m` : `${m}m`;
  }

  protected nameOf(employeeNo: string): string {
    return this.employees().find((e) => e.no === employeeNo)?.name ?? employeeNo;
  }

  /** The shift somebody is on now, for the assignment panel. */
  protected currentShift(employeeNo: string): string {
    const today = this.filterTo;

    return (
      this.assignments().find(
        (a) => a.employeeNo === employeeNo && a.fromDate <= today && (a.toDate === null || a.toDate >= today),
      )?.shiftCode ?? '—'
    );
  }

  /** Which days a shift runs, in short, from the bit-per-day encoding. */
  protected days(shift: ShiftRow): string {
    const names = ['Su', 'Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa'];

    return names.filter((_, index) => (shift.daysOfWeek & (1 << index)) !== 0).join(' ') || '—';
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);

    try {
      const [rows, shifts, assignments, employees] = await Promise.all([
        this.api.attendance(this.filterFrom, this.filterTo, this.filterEmployeeNo || undefined),
        this.api.shifts(),
        this.api.shiftAssignments(),
        this.employees().length > 0 ? Promise.resolve(this.employees()) : this.api.employees(),
      ]);

      this.rows.set(rows);
      this.shifts.set(shifts);
      this.assignments.set(assignments);
      this.employees.set(employees);
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected async record(amend = false): Promise<void> {
    if (!this.draftEmployeeNo || !this.draftDate) {
      return;
    }

    this.busy.set(true);

    try {
      const result = await this.api.recordAttendance(this.draftEmployeeNo, {
        onDate: this.draftDate,
        clockedInAt: this.draftIn || null,
        clockedOutAt: this.draftOut || null,
        note: this.draftNote || null,
        amend,
      });

      // The warnings are the point of the exercise — in on a day of leave, in on a rest day, on
      // no shift at all. Swallowing them would leave the day recorded and the oddity unsaid.
      this.messages.showAll((result.messages ?? []).filter((m) => m.severity !== 'Success'));

      this.messages.showSuccess(
        this.t('hr.attendance.recorded', {
          EmployeeNo: this.draftEmployeeNo,
          OnDate: this.draftDate,
        }),
      );

      this.draftIn = '';
      this.draftOut = '';
      this.draftNote = '';

      await this.reload();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async assign(): Promise<void> {
    if (!this.assignEmployeeNo || !this.assignShiftCode || !this.assignFrom) {
      return;
    }

    this.busy.set(true);

    try {
      await this.api.assignShift(this.assignEmployeeNo, this.assignShiftCode, this.assignFrom);

      this.messages.showSuccess(
        this.t('hr.attendance.assigned', {
          EmployeeNo: this.assignEmployeeNo,
          ShiftCode: this.assignShiftCode,
        }),
      );

      await this.reload();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }
}

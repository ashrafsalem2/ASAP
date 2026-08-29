import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  ScheduleLayout,
  ScheduleLine,
  ScheduleReport,
  ScheduleSummary,
} from '../../core/api/asap-api.models';
import { ScheduleService } from '../../core/api/schedule.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * Statements somebody defined, run and edited.
 *
 * The two halves are on one screen on purpose. Editing a layout is guesswork until you see what
 * it produces — a range that selects nothing looks exactly like a range that selects the right
 * accounts, right up until the figure comes back nought. So the report is beside the rows, and
 * saving re-runs it.
 */
@Component({
  selector: 'asap-schedules',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './schedules.html',
  styleUrl: './finance.scss',
})
export class Schedules implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(ScheduleService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly schedules = signal<ScheduleSummary[]>([]);
  protected readonly layout = signal<ScheduleLayout | null>(null);
  protected readonly report = signal<ScheduleReport | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly editing = signal(false);

  protected selectedCode = '';
  protected from = '';
  protected to = '';

  protected readonly kinds = ['Accounts', 'Formula', 'Heading'] as const;
  protected readonly amountKinds = ['NetChange', 'BalanceAtDate'] as const;

  async ngOnInit(): Promise<void> {
    const year = new Date().getFullYear();

    this.from = `${year}-01-01`;
    this.to = `${year}-12-31`;

    this.loading.set(true);

    try {
      const list = await this.api.list();

      this.schedules.set(list);

      if (list.length > 0) {
        this.selectedCode = list[0].code;
        await this.open();
      }
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canEdit(): boolean {
    return this.auth.can('Finance.Schedule.Update');
  }

  /** The Arabic wording when the page is in Arabic and there is one, else the English. */
  protected pick(english: string, arabic?: string | null): string {
    return this.i18n.language() === 'ar' && arabic ? arabic : english;
  }

  protected async open(): Promise<void> {
    if (!this.selectedCode) {
      return;
    }

    this.busy.set(true);

    try {
      const [layout, report] = await Promise.all([
        this.api.layout(this.selectedCode),
        this.api.run(this.selectedCode, this.from, this.to),
      ]);

      this.layout.set(layout);
      this.report.set(report);
    } catch (error) {
      // A layout with a circle in it refuses to run and says which rows. The layout still loads,
      // so the rows can be fixed — which is the only way out of that state.
      this.messages.showError(error);

      try {
        this.layout.set(await this.api.layout(this.selectedCode));
        this.report.set(null);
      } catch {
        this.layout.set(null);
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected addRow(): void {
    const layout = this.layout();

    if (!layout) {
      return;
    }

    // Numbered in tens, which is the convention the shipped layouts use and the reason a row can
    // later be inserted between two others without renaming anything.
    const highest = layout.lines
      .map((l) => Number(l.rowNo.replace(/\D/g, '')))
      .filter((n) => !Number.isNaN(n))
      .reduce((a, b) => Math.max(a, b), 0);

    this.layout.set({
      ...layout,
      lines: [
        ...layout.lines,
        {
          rowNo: `R${highest + 10}`,
          description: '',
          kind: 'Accounts',
          expression: '',
          descriptionArabic: null,
          amountKind: 'NetChange',
          showOppositeSign: false,
          indent: 0,
          isBold: false,
          hideIfZero: false,
        },
      ],
    });
  }

  protected removeRow(line: ScheduleLine): void {
    const layout = this.layout();

    if (!layout) {
      return;
    }

    this.layout.set({ ...layout, lines: layout.lines.filter((l) => l !== line) });
  }

  protected move(line: ScheduleLine, by: number): void {
    const layout = this.layout();

    if (!layout) {
      return;
    }

    const lines = [...layout.lines];
    const at = lines.indexOf(line);
    const to = at + by;

    if (at < 0 || to < 0 || to >= lines.length) {
      return;
    }

    [lines[at], lines[to]] = [lines[to], lines[at]];
    this.layout.set({ ...layout, lines });
  }

  protected async save(): Promise<void> {
    const layout = this.layout();

    if (!layout) {
      return;
    }

    this.busy.set(true);

    try {
      await this.api.save(layout);
      this.messages.showSuccess(this.t('finance.schedules.saved', { code: layout.code }));
      this.editing.set(false);

      await this.open();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }
}

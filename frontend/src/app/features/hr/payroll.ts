import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Branch, PayrollRun, PayrollRunSummary } from '../../core/api/asap-api.models';
import { HrService } from '../../core/api/hr.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * Payroll runs, and where each wage lands.
 *
 * The branch split is the part worth showing on screen rather than only in the ledger. Somebody
 * looking at a run wants to know two things: what it will pay, and what it will do to each shop's
 * costs — and the second is the one an accounts screen usually cannot answer until a month later.
 */
@Component({
  selector: 'asap-payroll',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './payroll.html',
  styleUrl: './hr.scss',
})
export class Payroll implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly hr = inject(HrService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly runs = signal<PayrollRunSummary[]>([]);
  protected readonly branches = signal<Branch[]>([]);
  protected readonly selected = signal<PayrollRun | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);

  protected from = '';
  protected to = '';
  protected description = '';
  protected overrideReason = '';

  /** Whether the run on screen can still be posted or thrown away. */
  protected readonly isDraft = computed(() => this.selected()?.status === 'Draft');

  async ngOnInit(): Promise<void> {
    const now = new Date();

    this.from = this.iso(new Date(now.getFullYear(), now.getMonth(), 1));
    this.to = this.iso(new Date(now.getFullYear(), now.getMonth() + 1, 0));

    await this.reload();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canWrite(): boolean {
    return this.auth.can('Hr.Wage.Update');
  }

  protected statusLabel(status: string): string {
    return this.t(`hr.status.${status}` as TranslationKey);
  }

  /** A branch by its key, falling back to the key so a row never silently loses it. */
  protected branchName(branchId: string): string {
    const branch = this.branches().find((b) => b.id === branchId);

    if (!branch) {
      return branchId;
    }

    return this.i18n.language() === 'ar' && branch.nameArabic ? branch.nameArabic : branch.name;
  }

  protected async select(run: PayrollRunSummary): Promise<void> {
    try {
      this.selected.set(await this.hr.payrollRun(run.no));
    } catch (error) {
      this.messages.showError(error);
    }
  }

  protected async calculate(): Promise<void> {
    if (!this.from || !this.to) {
      return;
    }

    this.busy.set('calculate');

    try {
      const saved = await this.hr.calculate({
        from: this.from,
        to: this.to,
        description: this.description.trim() || null,
      });

      this.messages.showAll(saved.messages);
      this.messages.showSuccess(
        this.t('hr.payroll.calculated', {
          no: saved.run.no,
          people: saved.run.lines.length,
        }),
      );

      this.selected.set(saved.run);
      await this.reload();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(null);
    }
  }

  protected async post(): Promise<void> {
    const run = this.selected();

    if (!run) {
      return;
    }

    this.busy.set('post');

    try {
      const saved = await this.hr.post(run.no, this.overrideReason.trim() || undefined);

      this.messages.showAll(saved.messages);
      this.messages.showSuccess(
        this.t('hr.payroll.posted', {
          no: saved.run.no,
          transactionNo: saved.run.transactionNo ?? '',
        }),
      );

      this.overrideReason = '';
      this.selected.set(saved.run);
      await this.reload();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(null);
    }
  }

  protected async discard(): Promise<void> {
    const run = this.selected();

    if (!run || !confirm(this.t('hr.payroll.discardConfirm', { no: run.no }))) {
      return;
    }

    this.busy.set('discard');

    try {
      await this.hr.discard(run.no);

      this.messages.showSuccess(this.t('hr.payroll.discarded', { no: run.no }));
      this.selected.set(null);
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
      const [runs, branches] = await Promise.all([this.hr.payrollRuns(), this.hr.branches()]);

      this.runs.set(runs);
      this.branches.set(branches);
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  private iso(date: Date): string {
    // Built from the local parts rather than toISOString, which shifts to UTC and can move a
    // month boundary by a day -- and the boundary is exactly what a payroll period is.
    const month = `${date.getMonth() + 1}`.padStart(2, '0');
    const day = `${date.getDate()}`.padStart(2, '0');

    return `${date.getFullYear()}-${month}-${day}`;
  }
}

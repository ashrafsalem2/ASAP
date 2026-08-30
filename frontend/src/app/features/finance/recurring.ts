import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RecurringBatch, RecurringLine } from '../../core/api/asap-api.models';
import { RecurringService } from '../../core/api/recurring.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * The journals that post themselves every month, and when each is next due.
 *
 * The due date is the column worth having on the list. A month end is a checklist, and the useful
 * question is not "what recurring batches exist" but "what have I not done yet" — so batches that
 * are due are marked, and the date they are due sorts them.
 */
@Component({
  selector: 'asap-recurring',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './recurring.html',
  styleUrl: './finance.scss',
})
export class Recurring implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(RecurringService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly batches = signal<RecurringBatch[]>([]);
  protected readonly selected = signal<RecurringBatch | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);

  protected postOn = new Date().toISOString().slice(0, 10);

  protected readonly methods = [
    'Fixed',
    'Variable',
    'Balance',
    'ReversingFixed',
    'ReversingVariable',
  ] as const;

  ngOnInit(): Promise<void> {
    return this.reload();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canEdit(): boolean {
    return this.auth.can('Finance.Journal.Create');
  }

  protected canPost(): boolean {
    return this.auth.can('Finance.Journal.Post');
  }

  protected name(batch: RecurringBatch): string {
    return this.i18n.language() === 'ar' && batch.nameArabic ? batch.nameArabic : batch.name;
  }

  /** Whether anything in the batch is due on or before the day chosen to post for. */
  protected isDue(batch: RecurringBatch): boolean {
    return batch.nextDue !== null && batch.nextDue <= this.postOn;
  }

  protected select(batch: RecurringBatch): void {
    // A copy, so abandoning an edit leaves the list as it was.
    this.selected.set(structuredClone(batch));
  }

  protected addLine(): void {
    const batch = this.selected();

    if (!batch) {
      return;
    }

    this.selected.set({
      ...batch,
      lines: [
        ...batch.lines,
        {
          accountNo: '',
          description: '',

          // The month-end recurrence, because it is the one almost every line wants and the one
          // that is fiddly to write from memory.
          recurrenceFormula: '1M+CM',
          amount: 0,
          method: 'Fixed',
          balancingAccountNo: null,
          nextPostingDate: this.postOn,
          expiresOn: null,
          dimensions: null,
        },
      ],
    });
  }

  protected removeLine(line: RecurringLine): void {
    const batch = this.selected();

    if (!batch) {
      return;
    }

    this.selected.set({ ...batch, lines: batch.lines.filter((l) => l !== line) });
  }

  protected async save(): Promise<void> {
    const batch = this.selected();

    if (!batch) {
      return;
    }

    this.busy.set(true);

    try {
      await this.api.save(batch);
      this.messages.showSuccess(this.t('finance.recurring.saved', { code: batch.code }));

      await this.reload();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async post(batch: RecurringBatch): Promise<void> {
    this.busy.set(true);

    try {
      const result = await this.api.post(batch.code, this.postOn);

      // Nought lines posted is an ordinary outcome, not a failure: a batch asked for before it is
      // due, or one whose variable lines nobody filled in. Saying which happened is the useful
      // part, so the two read differently.
      this.messages.showSuccess(
        result.run.linesPosted === 0
          ? this.t('finance.recurring.nothingPosted', { code: batch.code })
          : this.t('finance.recurring.posted', {
              code: batch.code,
              lines: result.run.linesPosted,
              transaction: result.run.transactionNo ?? 0,
            }),
      );

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
      const list = await this.api.list();

      this.batches.set(list);

      const current = this.selected();

      if (current) {
        const again = list.find((b) => b.code === current.code);

        this.selected.set(again ? structuredClone(again) : null);
      }
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

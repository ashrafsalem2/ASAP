import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HeadcountRow } from '../../core/api/asap-api.models';
import { HrService } from '../../core/api/hr.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * How many people are at each branch, on a day.
 *
 * On a day rather than over a period, because headcount is a photograph and not a film: asking
 * "how many people did we have in March" has no single answer, and any report that gives one has
 * quietly chosen which day it meant.
 */
@Component({
  selector: 'asap-headcount',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './headcount.html',
  styleUrl: './hr.scss',
})
export class Headcount implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly hr = inject(HrService);
  private readonly messages = inject(MessageService);

  protected readonly rows = signal<HeadcountRow[]>([]);
  protected readonly loading = signal(true);

  protected on = new Date().toISOString().slice(0, 10);

  ngOnInit(): Promise<void> {
    return this.run();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  /** The branch, or a plain word for entries charged to none. */
  protected branchOf(row: { branchCode: string | null; branchName: string | null }): string {
    return row.branchName ?? this.t('hr.reports.noBranch');
  }

  /** Everybody, so the branches add up to something a reader can check. */
  protected total(): number {
    return this.rows().reduce((sum, row) => sum + row.count, 0);
  }

  protected async run(): Promise<void> {
    this.loading.set(true);

    try {
      this.rows.set(await this.hr.headcount(this.on));
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

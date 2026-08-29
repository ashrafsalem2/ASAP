import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BranchCostRow } from '../../core/api/asap-api.models';
import { HrService } from '../../core/api/hr.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * What each branch's staff cost, at contractual rates, on a day.
 *
 * Deliberately not the same figure as branch performance, which reports what was actually posted
 * over a period. This one is the run rate — what the contracts say the month will cost if nobody
 * joins, leaves or is paid overtime. Both are useful and confusing them is how a budget conversation
 * goes wrong, so they are separate reports with separate names.
 */
@Component({
  selector: 'asap-cost-by-branch',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './cost-by-branch.html',
  styleUrl: './hr.scss',
})
export class CostByBranch implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly hr = inject(HrService);
  private readonly messages = inject(MessageService);

  protected readonly rows = signal<BranchCostRow[]>([]);
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

  protected total(): number {
    return this.rows().reduce((sum, row) => sum + row.monthlyWageCost, 0);
  }

  protected async run(): Promise<void> {
    this.loading.set(true);

    try {
      this.rows.set(await this.hr.costByBranch(this.on));
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

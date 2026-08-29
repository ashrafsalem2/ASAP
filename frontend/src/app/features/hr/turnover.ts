import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Turnover } from '../../core/api/asap-api.models';
import { HrService } from '../../core/api/hr.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * How many people came and went over a period, and the rate it comes to.
 *
 * The rate is measured against the average of the opening and closing headcounts rather than
 * against either end. A shop that doubled in size over the year has two very different denominators
 * to choose from, and choosing either makes the rate say what whoever chose it wanted.
 */
@Component({
  selector: 'asap-turnover',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './turnover.html',
  styleUrl: './hr.scss',
})
export class TurnoverReport implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly hr = inject(HrService);
  private readonly messages = inject(MessageService);

  protected readonly report = signal<Turnover | null>(null);
  protected readonly loading = signal(true);

  protected fromDate = `${new Date().getFullYear()}-01-01`;
  protected toDate = `${new Date().getFullYear()}-12-31`;

  ngOnInit(): Promise<void> {
    return this.run();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  /** The rate as a percentage, which is how everybody reads it. */
  protected percent(rate: number): string {
    return `${(rate * 100).toFixed(1)}%`;
  }

  protected async run(): Promise<void> {
    this.loading.set(true);

    try {
      this.report.set(await this.hr.turnover(this.fromDate, this.toDate));
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

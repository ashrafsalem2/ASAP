import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PosReceiptSummary, PosSession } from '../../core/api/asap-api.models';
import { PosService } from '../../core/api/pos.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * Every turn at every till, and what each drawer came to.
 *
 * The column that matters is the variance, so it is the one the eye lands on: shown only when a
 * drawer has actually been counted, coloured when it disagrees, and never a confident zero on a
 * session still trading. A till repeatedly over is as much worth investigating as one repeatedly
 * short, so both directions are visible rather than an absolute difference.
 */
@Component({
  selector: 'asap-pos-sessions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './sessions.html',
  styleUrl: './pos.scss',
})
export class PosSessions implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly pos = inject(PosService);
  private readonly messages = inject(MessageService);

  protected readonly sessions = signal<PosSession[]>([]);
  protected readonly selected = signal<PosSession | null>(null);
  protected readonly receipts = signal<PosReceiptSummary[]>([]);
  protected readonly loading = signal(true);

  protected status = '';

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected async filter(): Promise<void> {
    await this.load();
  }

  protected async select(session: PosSession): Promise<void> {
    if (this.selected()?.no === session.no) {
      this.selected.set(null);
      this.receipts.set([]);

      return;
    }

    try {
      const detail = await this.pos.session(session.no);

      this.selected.set(detail.session);
      this.receipts.set(detail.receipts);
    } catch (error) {
      this.messages.showError(error);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);

    try {
      this.sessions.set(await this.pos.sessions(this.status ? { status: this.status } : {}));
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

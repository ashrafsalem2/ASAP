import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuditEntry } from '../../core/api/asap-api.models';
import { AdminService } from '../../core/api/admin.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * What was done, by whom, and every protection somebody pushed past.
 *
 * Every override message in this system ends with a sentence saying the override has been
 * recorded against somebody's name. It was true and unreadable: the rows were being written and
 * nothing served them. A promise made in a message to a user is the strongest kind there is, and
 * the reason for the overrides-only filter is that those rows are what the promise was about.
 */
@Component({
  selector: 'asap-audit-log',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './audit-log.html',
  styleUrl: './admin.scss',
})
export class AuditLog implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly admin = inject(AdminService);
  private readonly messages = inject(MessageService);

  protected readonly entries = signal<AuditEntry[]>([]);
  protected readonly limit = signal(0);
  protected readonly loading = signal(true);

  protected from = '';
  protected to = '';
  protected userName = '';
  protected overridesOnly = false;

  async ngOnInit(): Promise<void> {
    const now = new Date();
    const month = `${now.getMonth() + 1}`.padStart(2, '0');
    const day = `${now.getDate()}`.padStart(2, '0');

    this.to = `${now.getFullYear()}-${month}-${day}`;
    this.from = this.iso(new Date(now.getFullYear(), now.getMonth(), 1));

    await this.run();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected async run(): Promise<void> {
    this.loading.set(true);

    try {
      const page = await this.admin.auditLog({
        from: this.from || undefined,
        to: this.to || undefined,
        userName: this.userName.trim() || undefined,
        overridesOnly: this.overridesOnly || undefined,
      });

      this.entries.set(page.rows);
      this.limit.set(page.limit);
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  /** The timestamp without its seconds, which nobody reads and which make the column wide. */
  protected when(value: string): string {
    return value.slice(0, 16).replace('T', ' ');
  }

  private iso(date: Date): string {
    const month = `${date.getMonth() + 1}`.padStart(2, '0');
    const day = `${date.getDate()}`.padStart(2, '0');

    return `${date.getFullYear()}-${month}-${day}`;
  }
}

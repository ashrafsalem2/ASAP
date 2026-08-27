import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminService } from '../../core/api/admin.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * Changing your own password.
 *
 * Needs no permission: it is the one thing every account must be able to do, and an account
 * created by an administrator holds a password somebody else chose until this is used.
 */
@Component({
  selector: 'asap-change-password',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './change-password.html',
  styleUrl: './admin.scss',
})
export class ChangePassword {
  protected readonly i18n = inject(I18nService);
  private readonly admin = inject(AdminService);
  private readonly messages = inject(MessageService);

  protected readonly busy = signal(false);

  protected current = '';
  protected next = '';
  protected confirm = '';

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  /** Whether the two new ones differ, which is worth saying before the request is made. */
  protected mismatched(): boolean {
    return this.confirm.length > 0 && this.next !== this.confirm;
  }

  protected async change(): Promise<void> {
    if (!this.current || !this.next || this.mismatched()) {
      return;
    }

    this.busy.set(true);

    try {
      await this.admin.changeOwnPassword(this.current, this.next);

      this.messages.showSuccess(this.t('account.password.changed'));

      this.current = '';
      this.next = '';
      this.confirm = '';
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }
}

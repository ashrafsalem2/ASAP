import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApprovalLimit } from '../../core/api/asap-api.models';
import { PurchasingService } from '../../core/api/purchasing.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * Who may sign a purchase order, and for how much.
 *
 * The rule this screen exists to support is not on it: nobody may approve an order they raised,
 * whatever their limit says. An approval you can give yourself is not a control but a checkbox, and
 * the whole point of the step is that a second person looked.
 *
 * What is here is the arithmetic around that — the amounts, and the fact that somebody with no
 * limit at all approves nothing, because a system where unknown means unlimited answers "who can
 * approve this" with "whoever has not been set up yet".
 */
@Component({
  selector: 'asap-approval-limits',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './approval-limits.html',
})
export class ApprovalLimits implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(PurchasingService);
  private readonly messages = inject(MessageService);

  protected readonly limits = signal<ApprovalLimit[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);

  /** The one being edited or added. */
  protected userId = '';
  protected userName = '';
  protected displayName = '';
  protected maximumAmount = 0;

  async ngOnInit(): Promise<void> {
    try {
      this.limits.set(await this.api.approvalLimits(true));
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected edit(limit: ApprovalLimit): void {
    this.userId = limit.userId;
    this.userName = limit.userName;
    this.displayName = limit.displayName ?? '';
    this.maximumAmount = limit.maximumAmount;
  }

  protected async save(): Promise<void> {
    if (!this.userId.trim() || !this.userName.trim()) {
      return;
    }

    this.busy.set(true);

    try {
      await this.api.setApprovalLimit({
        userId: this.userId.trim(),
        userName: this.userName.trim(),
        displayName: this.displayName.trim() || null,
        maximumAmount: Number(this.maximumAmount) || 0,
        isActive: true,
      });

      this.limits.set(await this.api.approvalLimits(true));

      this.userId = '';
      this.userName = '';
      this.displayName = '';
      this.maximumAmount = 0;
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async setActive(limit: ApprovalLimit, active: boolean): Promise<void> {
    this.busy.set(true);

    try {
      await this.api.setApprovalLimit({ ...limit, isActive: active });
      this.limits.set(await this.api.approvalLimits(true));
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected who(limit: ApprovalLimit): string {
    return limit.displayName || limit.userName;
  }
}

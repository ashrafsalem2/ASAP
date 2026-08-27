import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PermissionSetInfo, UserAccount } from '../../core/api/asap-api.models';
import { AdminService } from '../../core/api/admin.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * User accounts and what each of them may do.
 *
 * A permission system nobody can see is a permission system that gets worked around: the way an
 * installation ends up with six people signed in as the administrator is that granting them
 * anything less was harder than not.
 */
@Component({
  selector: 'asap-users',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './users.html',
  styleUrl: './admin.scss',
})
export class Users implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly admin = inject(AdminService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly users = signal<UserAccount[]>([]);
  protected readonly sets = signal<PermissionSetInfo[]>([]);
  protected readonly selected = signal<UserAccount | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);

  /** Which sets the selected user would hold if saved. */
  protected readonly holding = signal<string[]>([]);

  protected newUserName = '';
  protected newDisplayName = '';
  protected newEmail = '';
  protected newPassword = '';
  protected resetPassword = '';

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canCreate(): boolean {
    return this.auth.can('Platform.User.Create');
  }

  protected canWrite(): boolean {
    return this.auth.can('Platform.User.Update');
  }

  protected select(user: UserAccount): void {
    this.selected.set(user);
    this.holding.set([...user.permissionSets]);
    this.resetPassword = '';
  }

  protected holds(code: string): boolean {
    return this.holding().includes(code);
  }

  protected toggleSet(code: string, on: boolean): void {
    this.holding.update((held) =>
      on ? [...new Set([...held, code])] : held.filter((c) => c !== code),
    );
  }

  protected async create(): Promise<void> {
    if (!this.newUserName.trim() || !this.newDisplayName.trim() || !this.newPassword) {
      return;
    }

    this.busy.set('create');

    try {
      const created = await this.admin.createUser({
        userName: this.newUserName.trim(),
        displayName: this.newDisplayName.trim(),
        temporaryPassword: this.newPassword,
        email: this.newEmail.trim() || null,
        permissionSetCodes: [],
      });

      this.messages.showSuccess(this.t('admin.users.created', { name: created.userName }));

      this.newUserName = '';
      this.newDisplayName = '';
      this.newEmail = '';
      this.newPassword = '';

      await this.reload();
      this.select(created);
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(null);
    }
  }

  protected async saveSets(): Promise<void> {
    const user = this.selected();

    if (!user) {
      return;
    }

    this.busy.set('sets');

    try {
      const saved = await this.admin.assignSets(user.userName, this.holding());

      this.messages.showSuccess(this.t('admin.users.setsSaved', { name: user.userName }));
      this.selected.set(saved);
      await this.reload();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(null);
    }
  }

  protected async setActive(user: UserAccount, active: boolean): Promise<void> {
    this.busy.set(user.userName);

    try {
      const saved = await this.admin.updateUser(user.userName, { isActive: active });

      this.messages.showSuccess(
        this.t(active ? 'admin.users.enabled' : 'admin.users.disabled', { name: user.userName }),
      );

      this.selected.set(saved);
      await this.reload();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(null);
    }
  }

  protected async reset(): Promise<void> {
    const user = this.selected();

    if (!user || !this.resetPassword) {
      return;
    }

    this.busy.set('reset');

    try {
      const saved = await this.admin.resetPassword(user.userName, this.resetPassword);

      this.messages.showSuccess(this.t('admin.users.reset', { name: user.userName }));
      this.resetPassword = '';
      this.selected.set(saved);
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
      const [users, sets] = await Promise.all([this.admin.users(), this.admin.permissionSets()]);

      this.users.set(users);
      this.sets.set(sets);

      const current = this.selected();

      if (current) {
        const fresh = users.find((u) => u.userName === current.userName);

        if (fresh) {
          this.selected.set(fresh);
          this.holding.set([...fresh.permissionSets]);
        }
      }
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PermissionInfo, PermissionSetInfo } from '../../core/api/asap-api.models';
import { AdminService } from '../../core/api/admin.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/** The permissions of one module, so a set can be assembled a module at a time. */
interface PermissionGroup {
  module: string;
  permissions: PermissionInfo[];
}

/**
 * Permission sets, and the catalogue of everything that can go in one.
 *
 * Every permission arrives with the sentence its module wrote about it. Assembling a set from a
 * list of keys is how somebody ends up granting `Finance.Account.Override` because it sounded
 * administrative.
 */
@Component({
  selector: 'asap-permission-sets',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './permission-sets.html',
  styleUrl: './admin.scss',
})
export class PermissionSets implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly admin = inject(AdminService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly sets = signal<PermissionSetInfo[]>([]);
  protected readonly permissions = signal<PermissionInfo[]>([]);
  protected readonly selected = signal<PermissionSetInfo | null>(null);
  protected readonly granting = signal<string[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);

  protected code = '';
  protected name = '';
  protected nameArabic = '';
  protected description = '';
  protected creating = false;

  protected readonly groups = computed<PermissionGroup[]>(() => {
    const groups: PermissionGroup[] = [];

    for (const permission of this.permissions()) {
      const existing = groups.find((g) => g.module === permission.module);

      if (existing) {
        existing.permissions.push(permission);
      } else {
        groups.push({ module: permission.module, permissions: [permission] });
      }
    }

    return groups;
  });

  /** Whether what is on screen can be written back. */
  protected readonly editable = computed(() => {
    const set = this.selected();

    return this.creating || (set !== null && !set.isSystemDefined);
  });

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canWrite(): boolean {
    return this.auth.can('Platform.PermissionSet.Create') ||
      this.auth.can('Platform.PermissionSet.Update');
  }

  protected select(set: PermissionSetInfo): void {
    this.creating = false;
    this.selected.set(set);
    this.granting.set([...set.permissions]);
    this.code = set.code;
    this.name = set.name;
    this.nameArabic = set.nameArabic ?? '';
    this.description = set.description ?? '';
  }

  /** Starts a new set, seeded from whatever is on screen: copying is how most sets are made. */
  protected startNew(): void {
    this.creating = true;
    this.selected.set(null);
    this.code = '';
    this.name = '';
    this.nameArabic = '';
    this.description = '';
  }

  protected grants(key: string): boolean {
    return this.granting().includes(key);
  }

  protected toggle(key: string, on: boolean): void {
    this.granting.update((held) =>
      on ? [...new Set([...held, key])] : held.filter((k) => k !== key),
    );
  }

  protected async save(): Promise<void> {
    if (!this.code.trim() || !this.name.trim()) {
      return;
    }

    this.busy.set('save');

    const request = {
      code: this.code.trim(),
      name: this.name.trim(),
      nameArabic: this.nameArabic.trim() || null,
      description: this.description.trim() || null,
      permissions: this.granting(),
    };

    try {
      if (this.creating) {
        await this.admin.createSet(request);
      } else {
        await this.admin.updateSet(request.code, request);
      }

      this.messages.showSuccess(this.t('admin.sets.saved', { code: request.code }));
      this.creating = false;
      await this.reload();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(null);
    }
  }

  protected async remove(set: PermissionSetInfo): Promise<void> {
    if (!confirm(this.t('admin.sets.removeConfirm', { code: set.code }))) {
      return;
    }

    this.busy.set(set.code);

    try {
      await this.admin.deleteSet(set.code);

      this.messages.showSuccess(this.t('admin.sets.removed', { code: set.code }));
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
      const [sets, permissions] = await Promise.all([
        this.admin.permissionSets(),
        this.admin.permissions(),
      ]);

      this.sets.set(sets);
      this.permissions.set(permissions);

      const current = this.selected();

      if (current) {
        const fresh = sets.find((s) => s.code === current.code);

        if (fresh) {
          this.select(fresh);
        }
      }
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

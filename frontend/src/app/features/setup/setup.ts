import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Setting } from '../../core/api/asap-api.models';
import { SetupService } from '../../core/api/setup.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/** A module's settings, with the group headings the modules themselves chose. */
interface SettingGroup {
  module: string;
  group: string;
  settings: Setting[];
}

/**
 * Every setting the installation has.
 *
 * Generated from what the modules declare rather than written by hand, which is the point of
 * declaring them: a setting cannot exist in code without a name, an explanation, a type, a
 * default and a permission, and because this screen reads the same declarations it cannot exist
 * without appearing here either. There is no hidden configuration to be told about by whoever
 * happens to remember it — including an extension's, which arrives under its own heading without
 * the extension building a screen.
 */
@Component({
  selector: 'asap-setup',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './setup.html',
  styleUrl: './setup.scss',
})
export class Setup implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly setup = inject(SetupService);
  private readonly messages = inject(MessageService);

  protected readonly settings = signal<Setting[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);
  protected readonly filter = signal('');

  /** What the user has typed but not yet saved, by key. */
  protected readonly edits = signal<Record<string, string>>({});

  protected readonly modules = computed(() =>
    [...new Set(this.settings().map((s) => s.module))].sort((a, b) => a.localeCompare(b)),
  );

  /** The settings that match the search, kept in the modules' own grouping. */
  protected readonly groups = computed<SettingGroup[]>(() => {
    const needle = this.filter().trim().toLowerCase();

    const matching = needle
      ? this.settings().filter(
          (s) =>
            s.key.toLowerCase().includes(needle) ||
            s.displayName.toLowerCase().includes(needle) ||
            s.description.toLowerCase().includes(needle),
        )
      : this.settings();

    const groups: SettingGroup[] = [];

    for (const setting of matching) {
      const existing = groups.find(
        (g) => g.module === setting.module && g.group === setting.group,
      );

      if (existing) {
        existing.settings.push(setting);
      } else {
        groups.push({ module: setting.module, group: setting.group, settings: [setting] });
      }
    }

    return groups;
  });

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  /** What is in the box: the pending edit, else what is set, else the default. */
  protected shown(setting: Setting): string {
    const pending = this.edits()[setting.key];

    return pending ?? setting.value ?? setting.defaultValue ?? '';
  }

  protected edited(setting: Setting): boolean {
    return this.edits()[setting.key] !== undefined;
  }

  protected onEdit(setting: Setting, value: string): void {
    this.edits.update((edits) => ({ ...edits, [setting.key]: value }));
  }

  protected onToggle(setting: Setting, checked: boolean): void {
    this.onEdit(setting, checked ? 'true' : 'false');
  }

  protected isTrue(setting: Setting): boolean {
    return this.shown(setting).toLowerCase() === 'true';
  }

  protected async save(setting: Setting): Promise<void> {
    const value = this.edits()[setting.key];

    if (value === undefined) {
      return;
    }

    await this.write(setting, value);
  }

  /**
   * Clears the setting so it follows the default again.
   *
   * Not the same as typing the default in. A cleared setting follows the default if the default
   * later changes; one that was set to the same text does not, and nothing on screen would ever
   * show the difference.
   */
  protected async clear(setting: Setting): Promise<void> {
    await this.write(setting, null);
  }

  private async write(setting: Setting, value: string | null): Promise<void> {
    this.busy.set(setting.key);

    try {
      const saved = await this.setup.change(setting.key, value);

      this.messages.showAll(saved.messages);
      this.messages.showSuccess(this.t('setup.saved', { name: setting.displayName }));

      this.edits.update((edits) => {
        const { [setting.key]: _removed, ...rest } = edits;

        return rest;
      });

      this.settings.update((settings) =>
        settings.map((s) =>
          s.key === setting.key ? { ...s, value: saved.value, isSet: saved.isSet } : s,
        ),
      );
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(null);
    }
  }

  private async reload(): Promise<void> {
    this.loading.set(true);

    try {
      this.settings.set(await this.setup.settings());
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

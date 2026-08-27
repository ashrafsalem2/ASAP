import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ModuleReference, ReferenceEvent, ReferenceSummary } from '../../core/api/asap-api.models';
import { ReferenceService } from '../../core/api/reference.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/** Which part of a module is being read. */
type ReferenceTab = 'messages' | 'permissions' | 'settings' | 'menu';

/**
 * The developer reference, generated from what this installation declares.
 *
 * Not written by hand from the declarations — read from the same registries the running system
 * uses. A transcription is out of date the first time somebody adds a message and forgets the
 * document; this cannot be out of date without the system being wrong.
 *
 * It describes this installation, extensions included, which is the answer to "what can I
 * actually integrate with" for somebody holding a deployment rather than a source tree.
 */
@Component({
  selector: 'asap-reference',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './reference.html',
  styleUrl: './reference.scss',
})
export class Reference implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly reference = inject(ReferenceService);
  private readonly messages = inject(MessageService);

  protected readonly summary = signal<ReferenceSummary | null>(null);
  protected readonly module = signal<ModuleReference | null>(null);
  protected readonly events = signal<ReferenceEvent[]>([]);
  protected readonly tab = signal<ReferenceTab>('messages');
  protected readonly loading = signal(true);

  protected readonly tabs: readonly ReferenceTab[] = [
    'messages',
    'permissions',
    'settings',
    'menu',
  ];

  async ngOnInit(): Promise<void> {
    this.loading.set(true);

    try {
      const [summary, events] = await Promise.all([
        this.reference.summary(),
        this.reference.events(),
      ]);

      this.summary.set(summary);
      this.events.set(events);

      if (summary.modules.length > 0) {
        await this.open(summary.modules[0].moduleId);
      }
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected tabLabel(tab: ReferenceTab): string {
    return this.t(`reference.tab.${tab}` as TranslationKey);
  }

  protected async open(moduleId: string): Promise<void> {
    try {
      this.module.set(await this.reference.module(moduleId));
    } catch (error) {
      this.messages.showError(error);
    }
  }

  /** A placeholder as it appears in a message, which is what an integrator matches on. */
  protected braced(name: string): string {
    return `{${name}}`;
  }
}

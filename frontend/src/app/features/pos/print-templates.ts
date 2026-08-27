import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PrintTemplate, PrintTemplateField } from '../../core/api/asap-api.models';
import { PosService } from '../../core/api/pos.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * The receipt layout, edited by whoever runs the shop.
 *
 * The preview renders against a real posted receipt rather than an invented one. A layout that
 * looks right beside made-up figures is how a receipt ships with a total column too narrow for
 * four digits, and the first anybody knows about it is a queue.
 */
@Component({
  selector: 'asap-print-templates',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './print-templates.html',
  styleUrl: './print-templates.scss',
})
export class PrintTemplates implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly pos = inject(PosService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly templates = signal<PrintTemplate[]>([]);
  protected readonly fields = signal<PrintTemplateField[]>([]);
  protected readonly preview = signal<string>('');
  protected readonly previewOf = signal<string | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);

  protected code = '';
  protected name = '';
  protected content = '';
  protected width = 42;

  /** The fields grouped by the region they belong to, so the list reads like the template. */
  protected readonly groups = computed(() => {
    const groups: { region: string; fields: string[] }[] = [];

    for (const field of this.fields()) {
      const region = field.region || 'document';
      const existing = groups.find((g) => g.region === region);

      if (existing) {
        existing.fields.push(field.field);
      } else {
        groups.push({ region, fields: [field.field] });
      }
    }

    return groups;
  });

  /**
   * A field name as it is written in a template.
   *
   * Built here rather than in the markup: braces are what an Angular template is made of, and
   * escaping them into a literal pair reads as a puzzle rather than as an example.
   */
  protected braced(field: string): string {
    return `{${field}}`;
  }

  /** A ruler the width of the paper, so somebody can see where the edge is. */
  protected readonly ruler = computed(() => '─'.repeat(this.width));

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canWrite(): boolean {
    return this.auth.can('Pos.Station.Update');
  }

  protected select(template: PrintTemplate): void {
    this.code = template.code;
    this.name = template.name;
    this.content = template.content;
    this.width = template.widthInCharacters;

    void this.render();
  }

  /** Renders what is in the editor now, saved or not. */
  protected async render(): Promise<void> {
    if (!this.content) {
      this.preview.set('');
      return;
    }

    try {
      const rendered = await this.pos.previewTemplate(this.content, this.width);

      this.preview.set(rendered.text);
      this.previewOf.set(rendered.receiptNo);
    } catch (error) {
      this.messages.showError(error);
    }
  }

  protected async save(): Promise<void> {
    if (!this.code.trim() || !this.name.trim() || !this.content) {
      return;
    }

    this.busy.set(true);

    try {
      await this.pos.saveTemplate({
        code: this.code.trim(),
        name: this.name.trim(),
        content: this.content,
        widthInCharacters: this.width,
        isDefault: true,
      });

      this.messages.showSuccess(this.t('pos.templates.saved', { code: this.code }));
      await this.reload();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  /**
   * Sends the rendered receipt to the browser's own print dialog.
   *
   * No bridge agent, no driver, no configuration. It will not open a cash drawer or cut the
   * paper, and it prints on whatever the machine already has — which for a shop wanting a
   * receipt today is the difference between having one and waiting for an install.
   */
  protected printPreview(): void {
    const text = this.preview();

    if (!text) {
      return;
    }

    const window_ = window.open('', '_blank', 'width=420,height=640');

    if (!window_) {
      return;
    }

    // A monospace block at the paper's width, which is what a receipt printer produces.
    const escaped = text
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;');

    window_.document.write(
      `<pre style="font:12px/1.35 ui-monospace,Menlo,Consolas,monospace;margin:0">${escaped}</pre>`,
    );

    window_.document.close();
    window_.focus();
    window_.print();
  }

  private async reload(): Promise<void> {
    this.loading.set(true);

    try {
      const listed = await this.pos.printTemplates();

      this.templates.set(listed.templates);
      this.fields.set(listed.fields);

      if (!this.code && listed.templates.length > 0) {
        this.select(listed.templates[0]);
      }
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

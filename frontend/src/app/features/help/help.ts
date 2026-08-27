import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HelpPage, HelpTopicSummary } from '../../core/api/asap-api.models';
import { HelpService } from '../../core/api/help.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/** One stretch of a paragraph, and how it is emphasised. */
interface HelpRun {
  text: string;
  emphasis: 'strong' | 'em' | null;
}

/**
 * The help topics the messages point at.
 *
 * Nearly every refusal in ASAP carries a topic, and a link somebody follows at the moment they
 * are already stuck had better arrive somewhere. The topics come from the server in the reader's
 * language, and a conformance test refuses to let a message point at one that does not exist.
 */
@Component({
  selector: 'asap-help',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './help.html',
  styleUrl: './help.scss',
})
export class Help implements OnInit {
  /** The topic to open, taken from the route. */
  readonly topic = input<string>('');

  protected readonly i18n = inject(I18nService);
  private readonly help = inject(HelpService);
  private readonly messages = inject(MessageService);

  protected readonly topics = signal<HelpTopicSummary[]>([]);
  protected readonly page = signal<HelpPage | null>(null);
  protected readonly loading = signal(true);
  protected readonly filter = signal('');

  /** The topics that match the search, grouped under the area they belong to. */
  protected readonly areas = computed(() => {
    const needle = this.filter().trim().toLowerCase();

    const matching = needle
      ? this.topics().filter(
          (t) =>
            t.topic.toLowerCase().includes(needle) || t.title.toLowerCase().includes(needle),
        )
      : this.topics();

    const areas: { area: string; topics: HelpTopicSummary[] }[] = [];

    for (const topic of matching) {
      const existing = areas.find((a) => a.area === topic.area);

      if (existing) {
        existing.topics.push(topic);
      } else {
        areas.push({ area: topic.area, topics: [topic] });
      }
    }

    return areas;
  });

  /**
   * The page as a list of blocks.
   *
   * Rendered from a handful of markdown shapes rather than by pulling in a parser: the topics are
   * written for this and use headings, paragraphs, bold and nothing else. A dependency to render
   * four constructs is a dependency to keep up to date for years.
   */
  protected readonly blocks = computed(() => {
    const markdown = this.page()?.markdown ?? '';
    const blocks: { kind: 'h1' | 'h2' | 'p'; text: string }[] = [];

    for (const raw of markdown.split('\n')) {
      const line = raw.trim();

      if (line.length === 0) {
        continue;
      }

      if (line.startsWith('## ')) {
        blocks.push({ kind: 'h2', text: line.slice(3) });
      } else if (line.startsWith('# ')) {
        blocks.push({ kind: 'h1', text: line.slice(2) });
      } else {
        blocks.push({ kind: 'p', text: line });
      }
    }

    return blocks;
  });

  async ngOnInit(): Promise<void> {
    this.loading.set(true);

    try {
      this.topics.set(await this.help.topics());

      if (this.topic()) {
        await this.open(this.topic());
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

  protected async open(topic: string): Promise<void> {
    try {
      this.page.set(await this.help.page(topic));
    } catch (error) {
      this.messages.showError(error);
    }
  }

  /** Whether the page came back in a language the reader did not ask for. */
  protected translated(): boolean {
    const page = this.page();

    return page !== null && page.language !== page.requestedLanguage;
  }

  /**
   * Splits a paragraph into plain, bold and italic runs.
   *
   * Two markers rather than one, because the topics use both and a renderer that only knew about
   * bold printed the asterisks around anything italic — which is worse than not supporting it,
   * since it looks like a mistake in the writing rather than in the reader.
   */
  protected runs(text: string): HelpRun[] {
    const runs: HelpRun[] = [];
    const pattern = /\*\*([^*]+)\*\*|\*([^*]+)\*/g;
    let at = 0;

    for (const match of text.matchAll(pattern)) {
      if (match.index > at) {
        runs.push({ text: text.slice(at, match.index), emphasis: null });
      }

      runs.push(
        match[1] !== undefined
          ? { text: match[1], emphasis: 'strong' }
          : { text: match[2], emphasis: 'em' },
      );

      at = match.index + match[0].length;
    }

    if (at < text.length) {
      runs.push({ text: text.slice(at), emphasis: null });
    }

    return runs;
  }
}

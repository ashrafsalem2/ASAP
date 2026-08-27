import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { I18nService } from '../i18n/i18n.service';
import { ActiveMessage, MessageService } from './message.service';

/**
 * Renders the messages ASAP has raised.
 *
 * Each one shows what happened, why with the real figures in it, and what to do about it. That
 * third line is the reason this component exists rather than a one-line toast: the server goes to
 * the trouble of always producing a resolution, and a client that dropped it on the floor would
 * waste the whole design.
 */
@Component({
  selector: 'asap-message-centre',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (messages.messages().length > 0) {
      <div class="message-centre" role="status" aria-live="polite">
        @for (message of messages.messages(); track message.key) {
          <article class="message" [class]="'message--' + message.severity.toLowerCase()">
            <div class="message__body">
              <p class="message__title">{{ message.title }}</p>

              @if (message.detail) {
                <p class="message__detail">{{ message.detail }}</p>
              }

              @if (message.resolution) {
                <p class="message__resolution">
                  <span class="message__resolution-label">{{ resolutionLabel(message) }}</span>
                  {{ message.resolution }}
                </p>
              }

              <!-- The topic is followed at the moment somebody is already stuck, so it is a
                   link rather than a code they would have to look up. -->
              @if (message.helpTopic) {
                <button type="button" class="message__help" (click)="explain(message)">
                  {{ t('help.explain') }}
                </button>
              }

              @if (!isClientCode(message.code)) {
                <p class="message__code">{{ message.code }}</p>
              }
            </div>

            <button
              type="button"
              class="message__close"
              [attr.aria-label]="t('common.close')"
              (click)="messages.dismiss(message.key)"
            >
              &times;
            </button>
          </article>
        }
      </div>
    }
  `,
  styleUrl: './message-centre.scss',
})
export class MessageCentre {
  protected readonly messages = inject(MessageService);
  private readonly i18n = inject(I18nService);
  private readonly router = inject(Router);

  /**
   * Opens the topic behind a message, and dismisses the message.
   *
   * Dismissed because it has been read: leaving it behind the page that explains it means
   * arriving at an explanation with the complaint still on top of it.
   */
  protected explain(message: ActiveMessage): void {
    this.messages.dismiss(message.key);
    void this.router.navigate(['/help', message.helpTopic]);
  }

  protected t(key: Parameters<I18nService['translate']>[0]): string {
    return this.i18n.translate(key);
  }

  /**
   * "What to do" is wrong for an override: nothing is being asked of the reader, they are being
   * told what was allowed on their behalf and that it was written down.
   */
  protected resolutionLabel(message: ActiveMessage): string {
    return this.t(message.wasOverridden ? 'common.whatHappened' : 'common.whatToDo');
  }

  /**
   * Codes the client invented for its own confirmations. Printing one gives the reader a
   * reference that means nothing to anybody they might quote it to.
   */
  protected isClientCode(code: string): boolean {
    return code.startsWith('CLIENT.');
  }
}

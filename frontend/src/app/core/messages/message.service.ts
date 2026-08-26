import { Injectable, signal } from '@angular/core';
import { AsapMessage, messagesFromError } from '../api/asap-message';

/** A message currently on screen. */
export interface ActiveMessage extends AsapMessage {
  /** Distinguishes two occurrences of the same code. */
  key: number;
}

/**
 * Collects the messages ASAP raises and holds them until they are dealt with.
 *
 * How long a message stays is decided by what it is, not by a timer someone picked. Information
 * and success fade, because they confirm something the user already saw happen. Warnings, errors
 * and blocks stay until dismissed: a refusal that vanishes before it is read is a refusal the user
 * will hit again in ten seconds, and the second time they will not know why either.
 */
@Injectable({ providedIn: 'root' })
export class MessageService {
  private static readonly TransientLifetimeMs = 5000;

  private nextKey = 1;
  private readonly active = signal<ActiveMessage[]>([]);

  /** Messages currently on screen, oldest first. */
  readonly messages = this.active.asReadonly();

  /** Shows one message. */
  show(message: AsapMessage): void {
    const entry: ActiveMessage = { ...message, key: this.nextKey++ };

    this.active.update((messages) => [...messages, entry]);

    if (entry.severity === 'Information' || entry.severity === 'Success') {
      setTimeout(() => this.dismiss(entry.key), MessageService.TransientLifetimeMs);
    }
  }

  /** Shows several messages at once, which is the usual case for a refused posting. */
  showAll(messages: readonly AsapMessage[]): void {
    messages.forEach((message) => this.show(message));
  }

  /** Turns a failed request into messages and shows them. */
  showError(error: unknown, fallbackTitle = 'The request failed'): void {
    this.showAll(messagesFromError(error, fallbackTitle));
  }

  /** Shows a plain success confirmation for something the client did itself. */
  showSuccess(title: string, detail?: string): void {
    this.show({ code: 'CLIENT.OK', severity: 'Success', title, detail });
  }

  /** Removes one message. */
  dismiss(key: number): void {
    this.active.update((messages) => messages.filter((message) => message.key !== key));
  }

  /**
   * Clears everything.
   *
   * Called when a screen starts a fresh attempt, so the errors from the previous one do not sit
   * alongside the results of this one and leave the user unsure which is current.
   */
  clear(): void {
    this.active.set([]);
  }
}

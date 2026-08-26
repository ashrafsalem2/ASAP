/** How much weight a message carries. Mirrors the server's severities exactly. */
export type MessageSeverity = 'Information' | 'Success' | 'Warning' | 'Error' | 'Blocked';

/**
 * One thing ASAP has to tell the user, as the server rendered it.
 *
 * The text arrives translated and with the real figures already substituted, so the client never
 * builds a sentence of its own. That is deliberate: the message catalogue lives on the server, and
 * a client that composed its own wording would drift out of step with it within a release.
 */
export interface AsapMessage {
  /** Stable identifier, safe to branch on. The wording is not. */
  code: string;

  severity: MessageSeverity;

  /** One line saying what happened. */
  title: string;

  /** Why it happened, with the real values in it. */
  detail?: string;

  /** What to do next. Always present on a blocking message. */
  resolution?: string;

  /** The permission that would let someone push past this block. */
  overridePermission?: string;

  /** What the message is about, so the offending input can be marked up. */
  target?: {
    field?: string;
    entityType?: string;
    entityId?: string;
    displayNo?: string;
  };
}

/**
 * An RFC 9457 problem as ASAP returns it.
 *
 * The extension members are what make it useful. A bare problem carries a title and a detail
 * string, which would throw away the resolution, the override permission and the field at fault --
 * the parts the client needs to show something better than a red banner.
 */
export interface AsapProblem {
  type?: string;
  title: string;
  detail?: string;
  status?: number;
  instance?: string;

  code?: string;
  severity?: MessageSeverity;
  resolution?: string;
  overridePermission?: string;
  helpTopic?: string;
  traceId?: string;

  /** Every message, warnings included, not only the one that caused the failure. */
  messages?: AsapMessage[];

  /** The raw values behind the rendered text, for re-formatting in the client's locale. */
  arguments?: Record<string, unknown>;
}

/**
 * Turns whatever an HTTP failure produced into messages the client can display.
 *
 * Falls back through progressively less structured shapes, because a request can fail before it
 * ever reaches ASAP -- a dropped connection, a proxy error page -- and the user still deserves a
 * sentence rather than a spinner that never stops.
 */
export function messagesFromError(error: unknown, fallbackTitle: string): AsapMessage[] {
  const problem = (error as { error?: AsapProblem })?.error;

  if (problem?.messages?.length) {
    return problem.messages;
  }

  if (problem?.title) {
    return [
      {
        code: problem.code ?? 'HTTP.ERROR',
        severity: problem.severity ?? 'Error',
        title: problem.title,
        detail: problem.detail,
        resolution: problem.resolution,
      },
    ];
  }

  return [
    {
      code: 'CLIENT.REQUEST_FAILED',
      severity: 'Error',
      title: fallbackTitle,
      detail: (error as { message?: string })?.message,
    },
  ];
}

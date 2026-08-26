import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { MessageCentre } from './core/messages/message-centre';

/**
 * The application root.
 *
 * Holds only the router outlet and the message centre. The message centre sits here rather than in
 * the shell so that a refusal raised on the sign-in screen -- which is outside the shell -- is
 * shown the same way as one raised while posting a journal.
 */
@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, MessageCentre],
  template: `
    <router-outlet />
    <asap-message-centre />
  `,
})
export class App {}

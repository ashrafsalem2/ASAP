import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

@Component({
  selector: 'asap-login',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  template: `
    <main class="login">
      <form class="login__card panel" (ngSubmit)="submit()">
        <div class="login__brand">
          <span class="login__mark">A</span>
          <div>
            <h1>{{ t('app.name') }}</h1>
            <p class="login__tagline">{{ t('app.tagline') }}</p>
          </div>
        </div>

        <div class="field">
          <label class="field__label" for="userName">{{ t('auth.userName') }}</label>
          <input
            id="userName"
            name="userName"
            class="input"
            autocomplete="username"
            required
            [(ngModel)]="userName"
            [disabled]="busy()"
          />
        </div>

        <div class="field">
          <label class="field__label" for="password">{{ t('auth.password') }}</label>
          <input
            id="password"
            name="password"
            type="password"
            class="input"
            autocomplete="current-password"
            required
            [(ngModel)]="password"
            [disabled]="busy()"
          />
        </div>

        <button class="button button--primary login__submit" type="submit" [disabled]="busy()">
          @if (busy()) {
            <span class="spinner"></span>
            {{ t('auth.signingIn') }}
          } @else {
            {{ t('auth.signIn') }}
          }
        </button>

        <button type="button" class="button button--quiet login__language" (click)="i18n.toggle()">
          {{ t('shell.language') }}
        </button>
      </form>
    </main>
  `,
  styleUrl: './login.scss',
})
export class Login {
  protected readonly i18n = inject(I18nService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly messages = inject(MessageService);

  protected userName = '';
  protected password = '';
  protected readonly busy = signal(false);

  protected t(key: TranslationKey): string {
    return this.i18n.translate(key);
  }

  protected async submit(): Promise<void> {
    if (this.busy() || !this.userName || !this.password) {
      return;
    }

    // Clearing first means a second attempt does not sit underneath the failure from the first,
    // which would leave the user unsure which message is about the attempt they just made.
    this.messages.clear();
    this.busy.set(true);

    try {
      await this.auth.signIn(this.userName, this.password);

      const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl') ?? '/';
      await this.router.navigateByUrl(returnUrl);
    } catch (error) {
      this.messages.showError(error, this.t('auth.signIn'));
      this.password = '';
    } finally {
      this.busy.set(false);
    }
  }
}

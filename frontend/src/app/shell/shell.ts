import { ChangeDetectionStrategy, Component, OnInit, effect, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { FinanceService } from '../core/api/finance.service';
import { MenuNode } from '../core/api/asap-api.models';
import { AuthService } from '../core/auth/auth.service';
import { I18nService } from '../core/i18n/i18n.service';
import { TranslationKey } from '../core/i18n/translations';
import { MessageService } from '../core/messages/message.service';

/**
 * The application frame: header, menu, and whatever screen is open.
 *
 * The menu is not written here. It comes from the server, assembled from what every loaded module
 * declared and filtered to what this user may open in this company. That is what lets a module be
 * installed and appear in the menu without a line of client code changing -- and what makes a
 * cashier's menu six entries rather than sixty with fifty-four dead ends.
 */
@Component({
  selector: 'asap-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell implements OnInit {
  protected readonly auth = inject(AuthService);
  protected readonly i18n = inject(I18nService);
  private readonly finance = inject(FinanceService);
  private readonly messages = inject(MessageService);

  protected readonly menu = signal<MenuNode[]>([]);
  protected readonly menuOpen = signal(false);
  protected readonly switching = signal(false);

  constructor() {
    // Menu labels are rendered on the server in the caller's language, so switching language has
    // to fetch them again. Without this the shell turns Arabic while the menu stays English, which
    // reads as a half-finished translation rather than a deliberate design.
    effect(() => {
      this.i18n.language();
      void this.loadMenu();
    });
  }

  ngOnInit(): void {
    // The effect above already loads the menu on creation, so there is nothing to do here beyond
    // satisfying the lifecycle contract.
  }

  protected t(key: TranslationKey): string {
    return this.i18n.translate(key);
  }

  protected toggleMenu(): void {
    this.menuOpen.update((open) => !open);
  }

  protected closeMenu(): void {
    this.menuOpen.set(false);
  }

  /**
   * Moves to another company and rebuilds the menu.
   *
   * The menu has to be reloaded, not merely kept: permissions are held per company, so the same
   * person can be an accountant in one and read-only in the next, and a menu carried across would
   * offer screens that will refuse them.
   */
  protected async switchCompany(event: Event): Promise<void> {
    const companyId = (event.target as HTMLSelectElement).value;

    if (!companyId || companyId === this.auth.user()?.companyId) {
      return;
    }

    this.switching.set(true);

    try {
      await this.auth.switchCompany(companyId);
      await this.loadMenu();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.switching.set(false);
    }
  }

  protected signOut(): Promise<void> {
    return this.auth.signOut();
  }

  private async loadMenu(): Promise<void> {
    try {
      this.menu.set(await this.finance.navigation());
    } catch (error) {
      this.messages.showError(error);
      this.menu.set([]);
    }
  }
}

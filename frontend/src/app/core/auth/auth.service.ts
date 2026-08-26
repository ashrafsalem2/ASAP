import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { I18nService } from '../i18n/i18n.service';
import { Company, CurrentUser, SignInResponse } from '../api/asap-api.models';

const STORAGE_KEY = 'asap.session';

interface StoredSession {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
}

/**
 * Holds the signed-in session and everything that depends on it.
 *
 * Tokens are kept in local storage, which is a deliberate trade rather than an oversight. It is
 * readable by any script that gets onto the page, so a cross-site scripting hole becomes a stolen
 * session; an http-only cookie would not be. What local storage buys is a client that can be
 * served from anywhere and talk to an API anywhere, which is what a branch running against head
 * office needs. The mitigations are elsewhere and real: access tokens last fifteen minutes,
 * refresh tokens are single-use, and reusing one revokes the whole session.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly i18n = inject(I18nService);

  private readonly session = signal<StoredSession | null>(this.readStoredSession());
  private readonly currentUser = signal<CurrentUser | null>(null);
  private readonly availableCompanies = signal<Company[]>([]);

  /** True while a session is held. Does not guarantee the server still honours it. */
  readonly isSignedIn = computed(() => this.session() !== null);

  /** Who is signed in, once the server has been asked. */
  readonly user = this.currentUser.asReadonly();

  /** Companies the user may work in. */
  readonly companies = this.availableCompanies.asReadonly();

  /** The company being worked in. */
  readonly activeCompany = computed(() => {
    const companyId = this.currentUser()?.companyId;

    return this.availableCompanies().find((company) => company.id === companyId) ?? null;
  });

  /** The access token, for the interceptor to attach. */
  get accessToken(): string | null {
    return this.session()?.accessToken ?? null;
  }

  /** The refresh token, for the interceptor to redeem. */
  get refreshToken(): string | null {
    return this.session()?.refreshToken ?? null;
  }

  /** Whether the user holds a permission in the active company. */
  can(permission: string): boolean {
    const user = this.currentUser();

    if (!user) {
      return false;
    }

    return user.isSuperUser || user.permissions.includes(permission);
  }

  /** Signs in and loads everything the shell needs. */
  async signIn(userName: string, password: string): Promise<void> {
    const response = await firstValueFrom(
      this.http.post<SignInResponse>(`${environment.apiBaseUrl}/api/auth/login`, {
        userName,
        password,
      }),
    );

    this.store({
      accessToken: response.accessToken,
      refreshToken: response.refreshToken,
      expiresAt: response.expiresAt,
    });

    if (response.user.culture === 'ar' || response.user.culture === 'en') {
      this.i18n.set(response.user.culture);
    }

    await this.loadContext();
  }

  /**
   * Exchanges the refresh token for a new pair.
   *
   * Returns false rather than throwing when it fails, because the caller is the interceptor
   * retrying a request. A refusal there means the session is over, which is an outcome to act on
   * rather than an error to propagate.
   */
  async refresh(): Promise<boolean> {
    const token = this.refreshToken;

    if (!token) {
      return false;
    }

    try {
      const response = await firstValueFrom(
        this.http.post<SignInResponse>(`${environment.apiBaseUrl}/api/auth/refresh`, {
          refreshToken: token,
        }),
      );

      this.store({
        accessToken: response.accessToken,
        refreshToken: response.refreshToken,
        expiresAt: response.expiresAt,
      });

      return true;
    } catch {
      this.clear();
      return false;
    }
  }

  /**
   * Moves to another company.
   *
   * Done by refreshing the token rather than by setting a header, because the company claim in the
   * token is what the server's query filters trust. A client that could name its own company on
   * each request could name one it has no assignment in.
   */
  async switchCompany(companyId: string): Promise<void> {
    const token = this.refreshToken;

    if (!token) {
      return;
    }

    const response = await firstValueFrom(
      this.http.post<SignInResponse>(`${environment.apiBaseUrl}/api/auth/refresh`, {
        refreshToken: token,
        companyId,
      }),
    );

    this.store({
      accessToken: response.accessToken,
      refreshToken: response.refreshToken,
      expiresAt: response.expiresAt,
    });

    await this.loadContext();
  }

  /** Loads who the user is, what they may do, and which companies they can reach. */
  async loadContext(): Promise<void> {
    const [user, companies] = await Promise.all([
      firstValueFrom(this.http.get<CurrentUser>(`${environment.apiBaseUrl}/api/auth/me`)),
      firstValueFrom(this.http.get<Company[]>(`${environment.apiBaseUrl}/api/auth/companies`)),
    ]);

    this.currentUser.set(user);
    this.availableCompanies.set(companies);
  }

  /** Ends the session on the server as well as here. */
  async signOut(): Promise<void> {
    const token = this.refreshToken;

    if (token) {
      try {
        await firstValueFrom(
          this.http.post(`${environment.apiBaseUrl}/api/auth/logout`, { refreshToken: token }),
        );
      } catch {
        // The local session goes either way. A user who clicks sign out on a flaky connection
        // must not be left looking signed in.
      }
    }

    this.clear();
    await this.router.navigate(['/login']);
  }

  /** Drops the local session without calling the server. Used when a refresh is refused. */
  clear(): void {
    this.session.set(null);
    this.currentUser.set(null);
    this.availableCompanies.set([]);
    localStorage.removeItem(STORAGE_KEY);
  }

  private store(session: StoredSession): void {
    this.session.set(session);
    localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
  }

  private readStoredSession(): StoredSession | null {
    const raw = localStorage.getItem(STORAGE_KEY);

    if (!raw) {
      return null;
    }

    try {
      const parsed = JSON.parse(raw) as StoredSession;

      return parsed.accessToken && parsed.refreshToken ? parsed : null;
    } catch {
      // Corrupt storage should sign the user in again, not break the application shell.
      localStorage.removeItem(STORAGE_KEY);
      return null;
    }
  }
}

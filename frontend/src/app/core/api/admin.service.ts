import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { PermissionInfo, PermissionSetInfo, UserAccount } from './asap-api.models';

/** Talks to the administration endpoints: users, permission sets, and the permission catalogue. */
@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/admin`;

  /** Every permission the installed modules declare. */
  permissions(): Promise<PermissionInfo[]> {
    return firstValueFrom(this.http.get<PermissionInfo[]>(`${this.base}/permissions`));
  }

  /** User accounts and the sets each holds. */
  users(): Promise<UserAccount[]> {
    return firstValueFrom(this.http.get<UserAccount[]>(`${this.base}/users`));
  }

  /** Creates an account with a password its holder must change. */
  createUser(request: {
    userName: string;
    displayName: string;
    temporaryPassword: string;
    email?: string | null;
    permissionSetCodes?: string[];
  }): Promise<UserAccount> {
    return firstValueFrom(this.http.post<UserAccount>(`${this.base}/users`, request));
  }

  /** Changes a user's details, or turns the account off. */
  updateUser(
    userName: string,
    request: { displayName?: string; email?: string; isActive?: boolean; culture?: string },
  ): Promise<UserAccount> {
    return firstValueFrom(
      this.http.put<UserAccount>(`${this.base}/users/${encodeURIComponent(userName)}`, request),
    );
  }

  /** Replaces the sets a user holds. Anything not named is taken away. */
  assignSets(userName: string, permissionSetCodes: string[]): Promise<UserAccount> {
    return firstValueFrom(
      this.http.put<UserAccount>(
        `${this.base}/users/${encodeURIComponent(userName)}/permission-sets`,
        { permissionSetCodes },
      ),
    );
  }

  /** Gives somebody a new password they must change on first use. */
  resetPassword(userName: string, temporaryPassword: string): Promise<UserAccount> {
    return firstValueFrom(
      this.http.post<UserAccount>(
        `${this.base}/users/${encodeURIComponent(userName)}/reset-password`,
        { temporaryPassword },
      ),
    );
  }

  /** Permission sets and everything each grants. */
  permissionSets(): Promise<PermissionSetInfo[]> {
    return firstValueFrom(this.http.get<PermissionSetInfo[]>(`${this.base}/permission-sets`));
  }

  /** Creates a permission set. */
  createSet(request: {
    code: string;
    name: string;
    nameArabic?: string | null;
    description?: string | null;
    permissions: string[];
  }): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.base}/permission-sets`, request));
  }

  /** Rewrites what a set grants. Refused on a set ASAP maintains. */
  updateSet(
    code: string,
    request: {
      code: string;
      name: string;
      nameArabic?: string | null;
      description?: string | null;
      permissions: string[];
    },
  ): Promise<unknown> {
    return firstValueFrom(
      this.http.put(`${this.base}/permission-sets/${encodeURIComponent(code)}`, request),
    );
  }

  /** Removes a set nobody holds. */
  deleteSet(code: string): Promise<void> {
    return firstValueFrom(
      this.http.delete<void>(`${this.base}/permission-sets/${encodeURIComponent(code)}`),
    );
  }

  /** Changes the caller's own password, given the current one. */
  changeOwnPassword(currentPassword: string, newPassword: string): Promise<void> {
    return firstValueFrom(
      this.http.post<void>(`${environment.apiBaseUrl}/api/auth/change-password`, {
        currentPassword,
        newPassword,
      }),
    );
  }
}

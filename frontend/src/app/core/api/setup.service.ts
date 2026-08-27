import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Setting, SettingSaved } from './asap-api.models';

/** Talks to the setup endpoints, which are generated from what the modules declare. */
@Injectable({ providedIn: 'root' })
export class SetupService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/setup`;

  /** Every declared setting, its value, and whether the caller may change it. */
  settings(module?: string): Promise<Setting[]> {
    const params = module ? new HttpParams().set('module', module) : new HttpParams();

    return firstValueFrom(this.http.get<Setting[]>(this.base, { params }));
  }

  /** Changes one setting. A null value clears it back to the wider scope. */
  change(key: string, value: string | null): Promise<SettingSaved> {
    return firstValueFrom(
      this.http.put<SettingSaved>(`${this.base}/${encodeURIComponent(key)}`, { value }),
    );
  }
}

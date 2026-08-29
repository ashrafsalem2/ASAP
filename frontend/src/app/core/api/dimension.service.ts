import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { DimensionRow } from './asap-api.models';

/** Talks to the dimension endpoints. */
@Injectable({ providedIn: 'root' })
export class DimensionService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/dimensions`;

  /** The axes this company analyses by, and their values. */
  list(includeBlocked = false): Promise<DimensionRow[]> {
    const query = includeBlocked ? '?includeBlocked=true' : '';

    return firstValueFrom(this.http.get<DimensionRow[]>(`${this.base}${query}`));
  }

  /** Adds an axis or replaces one, values and all. */
  save(dimension: DimensionRow): Promise<unknown> {
    return firstValueFrom(
      this.http.put(`${this.base}/${encodeURIComponent(dimension.code)}`, dimension),
    );
  }
}

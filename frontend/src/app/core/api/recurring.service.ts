import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { RecurringBatch, RecurringLine, RecurringRun } from './asap-api.models';

/** Talks to the recurring journal endpoints. */
@Injectable({ providedIn: 'root' })
export class RecurringService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/finance/recurring`;

  /** The batches, their lines, and when each next falls due. */
  list(): Promise<RecurringBatch[]> {
    return firstValueFrom(this.http.get<RecurringBatch[]>(this.base));
  }

  /** Creates a batch or rewrites one, lines and all. */
  save(batch: {
    code: string;
    name: string;
    nameArabic?: string | null;
    description?: string | null;
    isActive?: boolean;
    lines: RecurringLine[];
  }): Promise<unknown> {
    return firstValueFrom(
      this.http.put(`${this.base}/${encodeURIComponent(batch.code)}`, batch),
    );
  }

  /** Posts every line that is due, and moves those lines on. */
  post(code: string, on?: string): Promise<{ run: RecurringRun; messages: unknown[] }> {
    const query = on ? `?on=${on}` : '';

    return firstValueFrom(
      this.http.post<{ run: RecurringRun; messages: unknown[] }>(
        `${this.base}/${encodeURIComponent(code)}/post${query}`,
        {},
      ),
    );
  }
}

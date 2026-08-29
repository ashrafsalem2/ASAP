import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  ScheduleLayout,
  ScheduleLine,
  ScheduleReport,
  ScheduleSummary,
} from './asap-api.models';

/** Talks to the statement layout endpoints. */
@Injectable({ providedIn: 'root' })
export class ScheduleService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/finance/schedules`;

  /** The layouts this company can run. */
  list(): Promise<ScheduleSummary[]> {
    return firstValueFrom(this.http.get<ScheduleSummary[]>(this.base));
  }

  /** One layout's rows, for editing. */
  layout(code: string): Promise<ScheduleLayout> {
    return firstValueFrom(
      this.http.get<ScheduleLayout>(`${this.base}/${encodeURIComponent(code)}/layout`),
    );
  }

  /** Runs a layout over a period. */
  run(code: string, from: string, to: string): Promise<ScheduleReport> {
    const query = new URLSearchParams({ from, to });

    return firstValueFrom(
      this.http.get<ScheduleReport>(`${this.base}/${encodeURIComponent(code)}?${query}`),
    );
  }

  /** Creates a layout or rewrites one, rows and all. */
  save(layout: {
    code: string;
    name: string;
    nameArabic?: string | null;
    description?: string | null;
    isActive?: boolean;
    lines: ScheduleLine[];
  }): Promise<unknown> {
    return firstValueFrom(
      this.http.put(`${this.base}/${encodeURIComponent(layout.code)}`, layout),
    );
  }
}

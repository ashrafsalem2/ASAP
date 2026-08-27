import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ModuleReference, ReferenceEvent, ReferenceSummary } from './asap-api.models';

/** Talks to the developer reference, which is generated from what the installation declares. */
@Injectable({ providedIn: 'root' })
export class ReferenceService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/reference`;

  /** The modules, and how much each brings. */
  summary(): Promise<ReferenceSummary> {
    return firstValueFrom(this.http.get<ReferenceSummary>(this.base));
  }

  /** Everything one module declares. */
  module(moduleId: string): Promise<ModuleReference> {
    return firstValueFrom(
      this.http.get<ModuleReference>(`${this.base}/modules/${encodeURIComponent(moduleId)}`),
    );
  }

  /** Every domain event an extension can subscribe to or raise. */
  events(): Promise<ReferenceEvent[]> {
    return firstValueFrom(this.http.get<ReferenceEvent[]>(`${this.base}/events`));
  }
}

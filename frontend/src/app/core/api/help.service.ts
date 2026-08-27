import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { HelpPage, HelpTopicSummary } from './asap-api.models';

/** Talks to the help endpoints, which serve the topics the messages point at. */
@Injectable({ providedIn: 'root' })
export class HelpService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/help`;

  /** Every topic, in the caller's language. */
  topics(): Promise<HelpTopicSummary[]> {
    return firstValueFrom(this.http.get<HelpTopicSummary[]>(this.base));
  }

  /** One topic. Falls back to English where it has not been translated. */
  page(topic: string, language?: 'en' | 'ar'): Promise<HelpPage> {
    const params = language ? new HttpParams().set('language', language) : new HttpParams();
    const path = topic.split('/').map(encodeURIComponent).join('/');

    return firstValueFrom(this.http.get<HelpPage>(`${this.base}/${path}`, { params }));
  }
}

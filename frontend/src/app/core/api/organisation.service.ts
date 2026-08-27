import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Branch, Companies } from './asap-api.models';

/** Talks to the company and branch endpoints. */
@Injectable({ providedIn: 'root' })
export class OrganisationService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api`;

  /** The companies in this tenant, and which one is current. */
  companies(): Promise<Companies> {
    return firstValueFrom(this.http.get<Companies>(`${this.base}/companies`));
  }

  /** The branches of the company being worked in. */
  branches(includeInactive = false): Promise<Branch[]> {
    const params = includeInactive
      ? new HttpParams().set('includeInactive', 'true')
      : new HttpParams();

    return firstValueFrom(this.http.get<Branch[]>(`${this.base}/branches`, { params }));
  }

  /** Creates a company, or changes the one with that code. */
  saveCompany(request: {
    code: string;
    name: string;
    nameArabic?: string | null;
    baseCurrencyCode?: string;
    registrationNo?: string | null;
    taxRegistrationNo?: string | null;
    fiscalYearStartMonth?: number;
    isActive?: boolean;
  }): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.base}/companies`, request));
  }

  /** Opens a branch, or changes the one with that code. */
  saveBranch(request: {
    code: string;
    name: string;
    nameArabic?: string | null;
    kind?: string;
    city?: string | null;
    address?: string | null;
    phone?: string | null;
    isActive?: boolean;
  }): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.base}/branches`, request));
  }
}

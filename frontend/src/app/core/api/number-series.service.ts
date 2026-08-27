import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { NumberSeriesInfo } from './asap-api.models';

/** Talks to the number series endpoints. */
@Injectable({ providedIn: 'root' })
export class NumberSeriesService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/number-series`;

  /** The series, their lines, and how many numbers each has left. */
  list(): Promise<NumberSeriesInfo[]> {
    return firstValueFrom(this.http.get<NumberSeriesInfo[]>(this.base));
  }

  /** Creates a series, or rewrites one that exists. Takes the whole set of lines. */
  save(request: {
    code: string;
    description: string;
    descriptionArabic?: string | null;
    allowGaps?: boolean;
    allowManualEntry?: boolean;
    enforceDateOrder?: boolean;
    isActive?: boolean;
    lines: {
      startingDate: string;
      startingNumber: string;
      endingNumber?: string | null;
      increment?: number;
      warnWhenRemainingBelow?: number | null;
      isOpen?: boolean;
    }[];
  }): Promise<unknown> {
    return firstValueFrom(this.http.post(this.base, request));
  }
}

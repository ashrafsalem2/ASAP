import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CurrencyInfo, ExchangeRateInfo } from './asap-api.models';

/** Talks to the currency and exchange rate endpoints. */
@Injectable({ providedIn: 'root' })
export class CurrencyService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/finance/currencies`;

  /** The currencies, and what each is worth today. */
  list(includeInactive = false): Promise<CurrencyInfo[]> {
    const query = includeInactive ? '?includeInactive=true' : '';

    return firstValueFrom(this.http.get<CurrencyInfo[]>(`${this.base}${query}`));
  }

  /** Adds a currency, or replaces one whole. */
  save(request: {
    code: string;
    name: string;
    nameArabic?: string | null;
    symbol?: string | null;
    decimalPlaces?: number;
    isActive?: boolean;
  }): Promise<CurrencyInfo> {
    return firstValueFrom(
      this.http.put<CurrencyInfo>(`${this.base}/${encodeURIComponent(request.code)}`, request),
    );
  }

  /** One currency's rates, most recent first. */
  rates(code: string): Promise<ExchangeRateInfo[]> {
    return firstValueFrom(
      this.http.get<ExchangeRateInfo[]>(`${this.base}/${encodeURIComponent(code)}/rates`),
    );
  }

  /** Enters the rate from a date, replacing any rate already starting on it. */
  saveRate(
    code: string,
    request: { startingDate: string; baseAmount: number; currencyAmount: number },
  ): Promise<ExchangeRateInfo> {
    return firstValueFrom(
      this.http.put<ExchangeRateInfo>(
        `${this.base}/${encodeURIComponent(code)}/rates`,
        request,
      ),
    );
  }
}

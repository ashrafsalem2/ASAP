import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  Offer,
  OfferMovementRow,
  OfferPreview,
  OfferSaved,
  OfferUptakeRow,
  SaveOfferRequest,
} from './asap-api.models';

/** Talks to the Promotions endpoints. */
@Injectable({ providedIn: 'root' })
export class PromotionsService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/promotions`;

  /** Offers, most recently starting first. */
  offers(activeOnly = false): Promise<Offer[]> {
    const params = activeOnly ? new HttpParams().set('activeOnly', 'true') : new HttpParams();

    return firstValueFrom(this.http.get<Offer[]>(`${this.base}/offers`, { params }));
  }

  /** One offer and what it applies to. */
  offer(offerCode: string): Promise<Offer> {
    return firstValueFrom(
      this.http.get<Offer>(`${this.base}/offers/${encodeURIComponent(offerCode)}`),
    );
  }

  /** Writes an offer, once it makes sense and clears the margin floor. */
  save(request: SaveOfferRequest): Promise<OfferSaved> {
    return firstValueFrom(this.http.post<OfferSaved>(`${this.base}/offers`, request));
  }

  /**
   * What an offer would do to every item it covers, at today's costs, without saving it.
   *
   * The reason this screen is worth building. Somebody choosing "twenty per cent off furniture"
   * is picking a percentage, not reading a cost sheet.
   */
  preview(request: SaveOfferRequest): Promise<OfferPreview> {
    return firstValueFrom(this.http.post<OfferPreview>(`${this.base}/offers/preview`, request));
  }

  /**
   * What each offer gave away and what it left.
   *
   * Offers that never applied are included: an offer nobody used is the most useful row in the
   * report, and one listing only what worked cannot say which campaign was a waste of a fortnight.
   */
  offerUptake(from: string, to: string): Promise<OfferUptakeRow[]> {
    const params = new HttpParams().set('from', from).set('to', to);

    return firstValueFrom(
      this.http.get<OfferUptakeRow[]>(`${this.base}/reports/uptake`, { params }),
    );
  }

  /**
   * How much of an offer's items moved during it, against the same length of time before.
   *
   * A comparison, and deliberately nothing stronger. Sales move for many reasons and none of them
   * are visible here.
   */
  offerMovement(offerCode: string): Promise<OfferMovementRow[]> {
    return firstValueFrom(
      this.http.get<OfferMovementRow[]>(
        `${this.base}/offers/${encodeURIComponent(offerCode)}/movement`,
      ),
    );
  }
}

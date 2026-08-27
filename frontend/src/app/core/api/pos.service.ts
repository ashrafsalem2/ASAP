import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  ParkedSale,
  PosReading,
  PosReceiptPosted,
  PosSession,
  PosSessionClosed,
  PosSessionDetail,
  PosStation,
  TenderKind,
} from './asap-api.models';

/** One thing being rung up. */
export interface PosLinePayload {
  type: 'Item' | 'GlAccount';
  no: string;
  quantity: number;
  unitPrice?: number;
  discountPercent?: number;
  description?: string;
  taxCode?: string;
}

/** Money put towards a receipt. */
export interface PosTenderPayload {
  kind: TenderKind;
  amount: number;
  reference?: string;
}

/** Talks to the point of sale endpoints. */
@Injectable({ providedIn: 'root' })
export class PosService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/pos`;

  /** The tills, each saying whether it is already open. */
  stations(): Promise<PosStation[]> {
    return firstValueFrom(this.http.get<PosStation[]>(`${this.base}/stations`));
  }

  /** Till sessions, most recently opened first. */
  sessions(filter: { stationCode?: string; status?: string } = {}): Promise<PosSession[]> {
    let params = new HttpParams();

    if (filter.stationCode) {
      params = params.set('stationCode', filter.stationCode);
    }

    if (filter.status) {
      params = params.set('status', filter.status);
    }

    return firstValueFrom(this.http.get<PosSession[]>(`${this.base}/sessions`, { params }));
  }

  /** One session and the receipts taken against it. */
  session(sessionNo: string): Promise<PosSessionDetail> {
    return firstValueFrom(
      this.http.get<PosSessionDetail>(`${this.base}/sessions/${encodeURIComponent(sessionNo)}`),
    );
  }

  /** Opens a drawer with a counted float. */
  open(stationCode: string, openingFloat: number): Promise<PosSession> {
    return firstValueFrom(
      this.http.post<PosSession>(`${this.base}/sessions`, { stationCode, openingFloat }),
    );
  }

  /**
   * Reads a session without closing it, which is an X reading.
   *
   * Available while trading on purpose: a supervisor checking a drawer mid-shift catches a
   * problem while it is still one receipt wide.
   */
  reading(sessionNo: string): Promise<PosReading> {
    return firstValueFrom(
      this.http.get<PosReading>(`${this.base}/sessions/${encodeURIComponent(sessionNo)}/reading`),
    );
  }

  /** Counts the drawer and finishes the session, which is a Z reading. */
  close(
    sessionNo: string,
    declaredCash: number,
    overrideReason?: string,
  ): Promise<PosSessionClosed> {
    return firstValueFrom(
      this.http.post<PosSessionClosed>(
        `${this.base}/sessions/${encodeURIComponent(sessionNo)}/close`,
        { declaredCash, overrideReason },
      ),
    );
  }

  /** What has been set aside and not paid for at this till. */
  parked(sessionNo: string): Promise<ParkedSale[]> {
    return firstValueFrom(
      this.http.get<ParkedSale[]>(
        `${this.base}/sessions/${encodeURIComponent(sessionNo)}/parked`,
      ),
    );
  }

  /** Sets a sale aside so the till can serve somebody else. Nothing posts. */
  park(
    sessionNo: string,
    lines: PosLinePayload[],
    parkedAs?: string,
    customerNo?: string,
  ): Promise<ParkedSale> {
    return firstValueFrom(
      this.http.post<ParkedSale>(
        `${this.base}/sessions/${encodeURIComponent(sessionNo)}/parked`,
        { lines, parkedAs, customerNo },
      ),
    );
  }

  /** Reads a parked sale back so the till can carry on with it. */
  recall(receiptNo: string): Promise<ParkedSale> {
    return firstValueFrom(
      this.http.get<ParkedSale>(`${this.base}/parked/${encodeURIComponent(receiptNo)}`),
    );
  }

  /** Throws a parked sale away. Voided rather than deleted, so the trail keeps it. */
  voidParked(receiptNo: string): Promise<ParkedSale> {
    return firstValueFrom(
      this.http.delete<ParkedSale>(`${this.base}/parked/${encodeURIComponent(receiptNo)}`),
    );
  }

  /** Rings a sale up, takes the money and posts everything. */
  postReceipt(
    sessionNo: string,
    lines: PosLinePayload[],
    tenders: PosTenderPayload[],
    options: {
      customerNo?: string;
      returnsReceiptNo?: string;
      parkedReceiptNo?: string;
      overrideReason?: string;
    } = {},
  ): Promise<PosReceiptPosted> {
    return firstValueFrom(
      this.http.post<PosReceiptPosted>(
        `${this.base}/sessions/${encodeURIComponent(sessionNo)}/receipts`,
        { lines, tenders, ...options },
      ),
    );
  }
}

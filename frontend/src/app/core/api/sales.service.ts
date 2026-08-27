import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  CreateSalesOrderRequest,
  SalesInvoiceResult,
  SalesOrder,
  SalesOrderCreated,
  SalesShipmentResult,
} from './asap-api.models';

/** How much of one line is going out, or being billed. */
export interface SalesLineQuantity {
  lineNo: number;
  quantity: number;
}

/** Talks to the Sales endpoints. */
@Injectable({ providedIn: 'root' })
export class SalesService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/sales`;

  /** Sales orders, most recently taken first. */
  orders(filter: { status?: string; customerNo?: string } = {}): Promise<SalesOrder[]> {
    let params = new HttpParams();

    if (filter.status) {
      params = params.set('status', filter.status);
    }

    if (filter.customerNo) {
      params = params.set('customerNo', filter.customerNo);
    }

    return firstValueFrom(this.http.get<SalesOrder[]>(`${this.base}/orders`, { params }));
  }

  /** One order and its lines. */
  order(orderNo: string): Promise<SalesOrder> {
    return firstValueFrom(
      this.http.get<SalesOrder>(`${this.base}/orders/${encodeURIComponent(orderNo)}`),
    );
  }

  /** Takes an order. Nothing posts until goods ship. */
  create(request: CreateSalesOrderRequest): Promise<SalesOrderCreated> {
    return firstValueFrom(this.http.post<SalesOrderCreated>(`${this.base}/orders`, request));
  }

  /** Confirms the order with the customer. */
  release(orderNo: string): Promise<SalesOrder> {
    return firstValueFrom(
      this.http.post<SalesOrder>(`${this.base}/orders/${encodeURIComponent(orderNo)}/release`, {}),
    );
  }

  /**
   * Records that goods left.
   *
   * Passing no lines ships everything still outstanding, which is the ordinary case and should
   * not need typing.
   */
  ship(
    orderNo: string,
    lines?: SalesLineQuantity[],
    overrideReason?: string,
  ): Promise<SalesShipmentResult> {
    return firstValueFrom(
      this.http.post<SalesShipmentResult>(
        `${this.base}/orders/${encodeURIComponent(orderNo)}/ship`,
        { lines, overrideReason },
      ),
    );
  }

  /** Turns what shipped into a debt the customer owes. */
  invoice(
    orderNo: string,
    lines?: SalesLineQuantity[],
    overrideReason?: string,
  ): Promise<SalesInvoiceResult> {
    return firstValueFrom(
      this.http.post<SalesInvoiceResult>(
        `${this.base}/orders/${encodeURIComponent(orderNo)}/invoice`,
        { lines, overrideReason },
      ),
    );
  }
}

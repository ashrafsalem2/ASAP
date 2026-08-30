import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  ApprovalLimit,
  CreatePurchaseOrderRequest,
  GoodsReceiptResult,
  PurchaseInvoiceResult,
  PurchaseOrder,
  PurchaseOrderCreated,
} from './asap-api.models';

/** How much of one line arrived, or is being invoiced. */
export interface PurchaseLineQuantity {
  lineNo: number;
  quantity: number;

  /** The price on the invoice, when it differs from the price ordered. */
  directUnitCost?: number;
}

/** Talks to the Purchasing endpoints. */
@Injectable({ providedIn: 'root' })
export class PurchasingService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/purchasing`;

  /** Purchase orders, most recently raised first. */
  orders(filter: { status?: string; vendorNo?: string } = {}): Promise<PurchaseOrder[]> {
    let params = new HttpParams();

    if (filter.status) {
      params = params.set('status', filter.status);
    }

    if (filter.vendorNo) {
      params = params.set('vendorNo', filter.vendorNo);
    }

    return firstValueFrom(this.http.get<PurchaseOrder[]>(`${this.base}/orders`, { params }));
  }

  /** One order and its lines. */
  order(orderNo: string): Promise<PurchaseOrder> {
    return firstValueFrom(
      this.http.get<PurchaseOrder>(`${this.base}/orders/${encodeURIComponent(orderNo)}`),
    );
  }

  /** Raises an order. Nothing posts until goods arrive. */
  create(request: CreatePurchaseOrderRequest): Promise<PurchaseOrderCreated> {
    return firstValueFrom(this.http.post<PurchaseOrderCreated>(`${this.base}/orders`, request));
  }

  /** Marks an order as sent to the vendor. */
  release(orderNo: string): Promise<PurchaseOrder> {
    return firstValueFrom(
      this.http.post<PurchaseOrder>(
        `${this.base}/orders/${encodeURIComponent(orderNo)}/release`,
        {},
      ),
    );
  }

  /**
   * Records that goods arrived.
   *
   * Passing no lines receives everything still outstanding, which is the ordinary case and
   * should not need typing.
   */
  receive(
    orderNo: string,
    lines?: PurchaseLineQuantity[],
    vendorDeliveryNo?: string,
    overrideReason?: string,
  ): Promise<GoodsReceiptResult> {
    return firstValueFrom(
      this.http.post<GoodsReceiptResult>(
        `${this.base}/orders/${encodeURIComponent(orderNo)}/receive`,
        { lines, vendorDeliveryNo, overrideReason },
      ),
    );
  }

  /** Turns what arrived into a debt owed to the vendor. */
  invoice(
    orderNo: string,
    vendorInvoiceNo: string,
    lines?: PurchaseLineQuantity[],
    overrideReason?: string,
  ): Promise<PurchaseInvoiceResult> {
    return firstValueFrom(
      this.http.post<PurchaseInvoiceResult>(
        `${this.base}/orders/${encodeURIComponent(orderNo)}/invoice`,
        { vendorInvoiceNo, lines, overrideReason },
      ),
    );
  }

  /** How much each person may sign a purchase order for. */
  approvalLimits(includeWithdrawn = false): Promise<ApprovalLimit[]> {
    const params = includeWithdrawn
      ? new HttpParams().set('includeWithdrawn', 'true')
      : new HttpParams();

    return firstValueFrom(
      this.http.get<ApprovalLimit[]>(`${this.base}/approval-limits`, { params }),
    );
  }

  /** Sets what one person may approve. */
  setApprovalLimit(limit: ApprovalLimit): Promise<ApprovalLimit> {
    return firstValueFrom(
      this.http.post<ApprovalLimit>(`${this.base}/approval-limits`, limit),
    );
  }

  /**
   * Signs for an order.
   *
   * Refused where the order is one you raised, whatever your limit says: the point of the step is
   * that a second person looked.
   */
  approveOrder(orderNo: string): Promise<unknown> {
    return firstValueFrom(
      this.http.post(`${this.base}/orders/${encodeURIComponent(orderNo)}/approve`, {}),
    );
  }

  /** Turns an order down, with a reason the buyer will read. */
  rejectOrder(orderNo: string, reason: string): Promise<unknown> {
    return firstValueFrom(
      this.http.post(`${this.base}/orders/${encodeURIComponent(orderNo)}/reject`, { reason }),
    );
  }
}

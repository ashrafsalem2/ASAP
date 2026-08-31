import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  CreateQuotationRequest,
  CreateRequisitionRequest,
  QuotationAnswerLine,
  QuotationAwardRequest,
  QuotationRequest,
  QuotationRequestSummary,
  PurchaseReturnResult,
  Requisition,
  RequisitionOrderLine,
  ApprovalLimit,
  OpenOrderRow,
  PurchaseAnalysisRow,
  CreatePurchaseOrderRequest,
  GoodsReceiptResult,
  PurchaseInvoiceResult,
  PurchaseOrder,
  PurchaseOrderCreated,
  VendorPerformanceRow,
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

  /**
   * Sends goods back to the vendor at what they cost, and credits what was invoiced.
   *
   * Nothing keyed means everything that could still go back. What is bounded here is what
   * arrived, not what was billed: goods can go back before their invoice ever turns up.
   */
  sendBack(
    orderNo: string,
    lines?: PurchaseLineQuantity[],
    reason?: string,
    overrideReason?: string,
  ): Promise<PurchaseReturnResult> {
    return firstValueFrom(
      this.http.post<PurchaseReturnResult>(
        `${this.base}/orders/${encodeURIComponent(orderNo)}/return`,
        { lines, reason, overrideReason },
      ),
    );
  }

  /** What vendors have been asked about, newest first. */
  quotationRequests(status?: string): Promise<QuotationRequestSummary[]> {
    let params = new HttpParams();

    if (status) {
      params = params.set('status', status);
    }

    return firstValueFrom(
      this.http.get<QuotationRequestSummary[]>(`${this.base}/quotations`, { params }),
    );
  }

  /** One request, with every quote for every line side by side. */
  quotationRequest(requestNo: string): Promise<QuotationRequest> {
    return firstValueFrom(
      this.http.get<QuotationRequest>(`${this.base}/quotations/${encodeURIComponent(requestNo)}`),
    );
  }

  /** Asks several vendors what something would cost. Commits nothing. */
  createQuotationRequest(request: CreateQuotationRequest): Promise<QuotationRequest> {
    return firstValueFrom(this.http.post<QuotationRequest>(`${this.base}/quotations`, request));
  }

  /** Adds vendors to ask. */
  inviteQuotationVendors(requestNo: string, vendorNos: string[]): Promise<QuotationRequest> {
    return this.actOnQuotation(requestNo, 'invite', { vendorNos });
  }

  /** Marks the request as gone out to the vendors. */
  sendQuotationRequest(requestNo: string): Promise<QuotationRequest> {
    return this.actOnQuotation(requestNo, 'send');
  }

  /** Records what one vendor said. Only from a vendor who was asked. */
  recordQuotation(
    requestNo: string,
    vendorNo: string,
    lines: QuotationAnswerLine[],
  ): Promise<QuotationRequest> {
    return this.actOnQuotation(requestNo, 'quote', { vendorNo, lines });
  }

  /** Records that a vendor is not quoting, and why. */
  declineQuotation(requestNo: string, vendorNo: string, reason?: string): Promise<QuotationRequest> {
    return this.actOnQuotation(requestNo, 'decline', { vendorNo, reason });
  }

  /**
   * Decides which vendor wins each line.
   *
   * Awarding anything other than the cheapest quote is refused without a reason — not because the
   * dearer supplier is wrong, but because that is the decision somebody asks about a year later.
   */
  awardQuotation(requestNo: string, awards: QuotationAwardRequest[]): Promise<QuotationRequest> {
    return this.actOnQuotation(requestNo, 'award', { awards });
  }

  /** Abandons a request that has produced no orders. */
  cancelQuotation(requestNo: string, reason?: string): Promise<QuotationRequest> {
    return this.actOnQuotation(requestNo, 'cancel', { reason });
  }

  /** Turns one vendor's awarded lines into an order at the price they quoted. */
  orderFromQuotation(
    requestNo: string,
    vendorNo: string,
    expectedReceiptDate?: string,
  ): Promise<PurchaseOrderCreated> {
    return firstValueFrom(
      this.http.post<PurchaseOrderCreated>(
        `${this.base}/quotations/${encodeURIComponent(requestNo)}/order`,
        { vendorNo, expectedReceiptDate },
      ),
    );
  }

  private actOnQuotation(
    requestNo: string,
    what: string,
    body: Record<string, unknown> = {},
  ): Promise<QuotationRequest> {
    return firstValueFrom(
      this.http.post<QuotationRequest>(
        `${this.base}/quotations/${encodeURIComponent(requestNo)}/${what}`,
        body,
      ),
    );
  }

  /** What has been asked for, newest first. */
  requisitions(status?: string): Promise<Requisition[]> {
    let params = new HttpParams();

    if (status) {
      params = params.set('status', status);
    }

    return firstValueFrom(this.http.get<Requisition[]>(`${this.base}/requisitions`, { params }));
  }

  /** One requisition and what is on it. */
  requisition(requisitionNo: string): Promise<Requisition> {
    return firstValueFrom(
      this.http.get<Requisition>(`${this.base}/requisitions/${encodeURIComponent(requisitionNo)}`),
    );
  }

  /** Asks for something to be bought. Commits nothing. */
  createRequisition(request: CreateRequisitionRequest): Promise<Requisition> {
    return firstValueFrom(this.http.post<Requisition>(`${this.base}/requisitions`, request));
  }

  /** Sends it for approval, or approves it where none is needed. */
  submitRequisition(requisitionNo: string): Promise<Requisition> {
    return this.actOnRequisition(requisitionNo, 'submit');
  }

  /** Signs for a requisition. Never your own. */
  approveRequisition(requisitionNo: string): Promise<Requisition> {
    return this.actOnRequisition(requisitionNo, 'approve');
  }

  /** Turns a requisition down, and says why. */
  rejectRequisition(requisitionNo: string, reason?: string): Promise<Requisition> {
    return this.actOnRequisition(requisitionNo, 'reject', { reason });
  }

  /** Abandons a requisition before it becomes anything. */
  cancelRequisition(requisitionNo: string, reason?: string): Promise<Requisition> {
    return this.actOnRequisition(requisitionNo, 'cancel', { reason });
  }

  /**
   * Turns part of an approved requisition into an order for one vendor.
   *
   * Called once per vendor: a requisition asking for paper, bolts and a kettle is one question
   * with three answers. The prices come from here rather than from the requisition, which carried
   * a guess.
   */
  orderFromRequisition(
    requisitionNo: string,
    vendorNo: string,
    lines?: RequisitionOrderLine[],
    expectedReceiptDate?: string,
  ): Promise<PurchaseOrderCreated> {
    return firstValueFrom(
      this.http.post<PurchaseOrderCreated>(
        `${this.base}/requisitions/${encodeURIComponent(requisitionNo)}/order`,
        { vendorNo, lines, expectedReceiptDate },
      ),
    );
  }

  private actOnRequisition(
    requisitionNo: string,
    what: string,
    body: Record<string, unknown> = {},
  ): Promise<Requisition> {
    return firstValueFrom(
      this.http.post<Requisition>(
        `${this.base}/requisitions/${encodeURIComponent(requisitionNo)}/${what}`,
        body,
      ),
    );
  }

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

  /** What is on order and has not arrived, most overdue first. */
  openOrders(overdueOnly = false, vendorNo?: string): Promise<OpenOrderRow[]> {
    let params = new HttpParams();

    if (overdueOnly) {
      params = params.set('overdueOnly', 'true');
    }

    if (vendorNo) {
      params = params.set('vendorNo', vendorNo);
    }

    return firstValueFrom(
      this.http.get<OpenOrderRow[]>(`${this.base}/reports/open-orders`, { params }),
    );
  }

  /**
   * How each vendor has actually behaved.
   *
   * Lateness is averaged over the late deliveries only, and a vendor who never promised a date is
   * counted separately rather than scored on time.
   */
  vendorPerformance(from: string, to: string): Promise<VendorPerformanceRow[]> {
    const params = new HttpParams().set('from', from).set('to', to);

    return firstValueFrom(
      this.http.get<VendorPerformanceRow[]>(`${this.base}/reports/vendor-performance`, { params }),
    );
  }

  /** What was bought over a period, by vendor or by item. */
  purchaseAnalysis(from: string, to: string, byItem = false): Promise<PurchaseAnalysisRow[]> {
    let params = new HttpParams().set('from', from).set('to', to);

    if (byItem) {
      params = params.set('byItem', 'true');
    }

    return firstValueFrom(
      this.http.get<PurchaseAnalysisRow[]>(`${this.base}/reports/purchase-analysis`, { params }),
    );
  }
}

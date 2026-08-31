import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  CreateSalesOrderRequest,
  CreateSalesQuoteRequest,
  CustomerGroupPriceList,
  CustomerPriceList,
  MarginRow,
  OpenSalesOrderRow,
  PriceList,
  ResolvedPrice,
  SalesInvoiceResult,
  SalesOrder,
  SalesOrderCreated,
  SalesQuote,
  SalesQuoteCreated,
  SalesReturnResult,
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

  /**
   * Takes goods back at what they cost and credits the customer.
   *
   * Nothing keyed means everything that could still come back, which is the ordinary case: a
   * customer returning the lot.
   */
  takeBack(
    orderNo: string,
    lines?: SalesLineQuantity[],
    reason?: string,
    overrideReason?: string,
  ): Promise<SalesReturnResult> {
    return firstValueFrom(
      this.http.post<SalesReturnResult>(
        `${this.base}/orders/${encodeURIComponent(orderNo)}/return`,
        { lines, reason, overrideReason },
      ),
    );
  }

  /**
   * Revenue, cost and margin by item, thinnest first.
   *
   * A row whose estimated cost is most of its cost is not reporting a margin, it is saying to come
   * back after the goods arrive.
   */
  marginByItem(from: string, to: string): Promise<MarginRow[]> {
    const params = new HttpParams().set('from', from).set('to', to);

    return firstValueFrom(
      this.http.get<MarginRow[]>(`${this.base}/reports/margin-by-item`, { params }),
    );
  }

  /** The same, by customer, gathering every channel a sale can come through. */
  marginByCustomer(from: string, to: string): Promise<MarginRow[]> {
    const params = new HttpParams().set('from', from).set('to', to);

    return firstValueFrom(
      this.http.get<MarginRow[]>(`${this.base}/reports/margin-by-customer`, { params }),
    );
  }

  /** What is ordered and has not shipped, most overdue first. */
  openSalesOrders(overdueOnly = false, customerNo?: string): Promise<OpenSalesOrderRow[]> {
    let params = new HttpParams();

    if (overdueOnly) {
      params = params.set('overdueOnly', 'true');
    }

    if (customerNo) {
      params = params.set('customerNo', customerNo);
    }

    return firstValueFrom(
      this.http.get<OpenSalesOrderRow[]>(`${this.base}/reports/open-orders`, { params }),
    );
  }

  /** The quotes offered, newest first. */
  quotes(status?: string, customerNo?: string): Promise<SalesQuote[]> {
    let params = new HttpParams();

    if (status) {
      params = params.set('status', status);
    }

    if (customerNo) {
      params = params.set('customerNo', customerNo);
    }

    return firstValueFrom(this.http.get<SalesQuote[]>(`${this.base}/quotes`, { params }));
  }

  /** One quote and what is on it. */
  quote(quoteNo: string): Promise<SalesQuote> {
    return firstValueFrom(
      this.http.get<SalesQuote>(`${this.base}/quotes/${encodeURIComponent(quoteNo)}`),
    );
  }

  /** Offers a customer a price, without promising any stock. */
  createQuote(request: CreateSalesQuoteRequest): Promise<SalesQuoteCreated> {
    return firstValueFrom(this.http.post<SalesQuoteCreated>(`${this.base}/quotes`, request));
  }

  /** Marks a quote as sent to the customer. */
  sendQuote(quoteNo: string): Promise<SalesQuote> {
    return firstValueFrom(
      this.http.post<SalesQuote>(`${this.base}/quotes/${encodeURIComponent(quoteNo)}/send`, {}),
    );
  }

  /**
   * Turns an accepted quote into an order.
   *
   * The prices go across exactly as quoted. What the price list holds today is beside the point:
   * the customer accepted the number in front of them.
   */
  acceptQuote(quoteNo: string, locationCode?: string): Promise<SalesOrderCreated> {
    return firstValueFrom(
      this.http.post<SalesOrderCreated>(
        `${this.base}/quotes/${encodeURIComponent(quoteNo)}/accept`,
        { locationCode },
      ),
    );
  }

  /** Records that the customer said no, and why. */
  declineQuote(quoteNo: string, reason?: string): Promise<SalesQuote> {
    return firstValueFrom(
      this.http.post<SalesQuote>(
        `${this.base}/quotes/${encodeURIComponent(quoteNo)}/decline`,
        { reason },
      ),
    );
  }

  /** Marks every quote that ran out without an answer. */
  expireQuotes(): Promise<{ expired: number }> {
    return firstValueFrom(
      this.http.post<{ expired: number }>(`${this.base}/quotes/expire`, {}),
    );
  }

  /** The agreed price lists and everything on them. */
  priceLists(): Promise<PriceList[]> {
    return firstValueFrom(this.http.get<PriceList[]>(`${this.base}/price-lists`));
  }

  /** Who is on which price list. */
  priceListAssignments(): Promise<CustomerPriceList[]> {
    return firstValueFrom(
      this.http.get<CustomerPriceList[]>(`${this.base}/price-lists/assignments`),
    );
  }

  /**
   * Writes a price list and everything on it.
   *
   * The lines given replace the lines held: a price list is edited as a whole sheet, because that
   * is how somebody negotiating a contract thinks about it.
   */
  savePriceList(list: PriceList): Promise<PriceList> {
    return firstValueFrom(
      this.http.put<PriceList>(`${this.base}/price-lists/${encodeURIComponent(list.code)}`, list),
    );
  }

  /** Which customer group is on which price list. */
  groupPriceListAssignments(): Promise<CustomerGroupPriceList[]> {
    return firstValueFrom(
      this.http.get<CustomerGroupPriceList[]>(`${this.base}/price-lists/group-assignments`),
    );
  }

  /** Puts a customer group on a price list, or takes it off one when the code is null. */
  assignGroupPriceList(customerGroupCode: string, priceListCode: string | null): Promise<void> {
    return firstValueFrom(
      this.http.put<void>(
        `${this.base}/price-lists/group-assignments/${encodeURIComponent(customerGroupCode)}`,
        { priceListCode },
      ),
    );
  }

  /** Puts a customer on a price list, or takes them off one when the code is null. */
  assignPriceList(customerNo: string, priceListCode: string | null): Promise<void> {
    return firstValueFrom(
      this.http.put<void>(
        `${this.base}/price-lists/assignments/${encodeURIComponent(customerNo)}`,
        { priceListCode },
      ),
    );
  }

  /** What one customer pays for one item on one day. */
  quotePrice(customerNo: string, itemNo: string, quantity: number): Promise<ResolvedPrice> {
    const params = new HttpParams()
      .set('customerNo', customerNo)
      .set('itemNo', itemNo)
      .set('quantity', quantity);

    return firstValueFrom(
      this.http.get<ResolvedPrice>(`${this.base}/price-lists/quote`, { params }),
    );
  }
}

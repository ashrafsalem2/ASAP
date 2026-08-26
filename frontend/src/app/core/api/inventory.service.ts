import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  Item,
  SettlementReceipt,
  StockLocation,
  StockMovement,
  StockMovementRequest,
  StockOnHandRow,
  StockPostingReceipt,
} from './asap-api.models';

/** Talks to the Inventory endpoints. */
@Injectable({ providedIn: 'root' })
export class InventoryService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/inventory`;

  /** The items in the active company. */
  items(): Promise<Item[]> {
    return firstValueFrom(this.http.get<Item[]>(`${this.base}/items`));
  }

  /** The locations stock can be held at. */
  locations(): Promise<StockLocation[]> {
    return firstValueFrom(this.http.get<StockLocation[]>(`${this.base}/locations`));
  }

  /** What is on hand, by item and location. */
  onHand(itemNo?: string): Promise<StockOnHandRow[]> {
    const params = itemNo ? new HttpParams().set('itemNo', itemNo) : undefined;

    return firstValueFrom(this.http.get<StockOnHandRow[]>(`${this.base}/stock/on-hand`, { params }));
  }

  /** Recorded movements, most recent transaction first. */
  movements(itemNo?: string): Promise<StockMovement[]> {
    const params = itemNo ? new HttpParams().set('itemNo', itemNo) : undefined;

    return firstValueFrom(this.http.get<StockMovement[]>(`${this.base}/stock/movements`, { params }));
  }

  /** Receives, issues or adjusts stock. */
  post(request: {
    movements: StockMovementRequest[];
    postingDate?: string;
    documentNo?: string;
    sourceCode?: string;
  }): Promise<StockPostingReceipt> {
    return firstValueFrom(this.http.post<StockPostingReceipt>(`${this.base}/stock/post`, request));
  }

  /** Settles estimated costs against what the goods actually cost. */
  settle(itemNo?: string): Promise<SettlementReceipt> {
    const params = itemNo ? new HttpParams().set('itemNo', itemNo) : undefined;

    return firstValueFrom(
      this.http.post<SettlementReceipt>(`${this.base}/stock/settle`, {}, { params }),
    );
  }
}

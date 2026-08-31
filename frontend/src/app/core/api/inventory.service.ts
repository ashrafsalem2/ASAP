import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  AdjustmentReason,
  CategoryPostingGap,
  AgeingRow,
  ReserveStockRequest,
  StockAvailabilityRow,
  StockReservationRow,
  ValuationRow,
  VelocityRow,
  Bin,
  BinContent,
  BinMovementResult,
  BinMovementRow,
  CreateTransferRequest,
  Item,
  ItemCategory,
  ItemUnit,
  ItemVariant,
  ResolvedQuantity,
  SettlementReceipt,
  StockCount,
  StockCountPosted,
  StockCountSummary,
  ReorderKind,
  ReorderPolicyRow,
  StockLocation,
  StockMovement,
  StockMovementRequest,
  StockOnHandRow,
  ShrinkageRow,
  StockPostingReceipt,
  Transfer,
  TransferCreated,
  TransferMoveReceipt,
  VariantStockRow,
  UnitOfMeasure,
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

  /** Stock counts, most recent first. */
  stockCounts(locationCode?: string): Promise<StockCountSummary[]> {
    const params = locationCode
      ? new HttpParams().set('locationCode', locationCode)
      : new HttpParams();

    return firstValueFrom(
      this.http.get<StockCountSummary[]>(`${this.base}/counts`, { params }),
    );
  }

  /** One count and its sheet. */
  stockCount(countNo: string): Promise<StockCount> {
    return firstValueFrom(
      this.http.get<StockCount>(`${this.base}/counts/${encodeURIComponent(countNo)}`),
    );
  }

  /** Starts a count and makes the sheet from what the system says now. */
  startStockCount(request: {
    locationCode: string;
    countDate?: string | null;
    description?: string | null;
    itemNos?: string[] | null;
  }): Promise<StockCount> {
    return firstValueFrom(this.http.post<StockCount>(`${this.base}/counts`, request));
  }

  /** Records what was found on a shelf. Null clears it back to uncounted. */
  recordStockCount(
    countNo: string,
    itemNo: string,
    countedQuantity: number | null,
    note?: string,
  ): Promise<StockCount> {
    return firstValueFrom(
      this.http.post<StockCount>(`${this.base}/counts/${encodeURIComponent(countNo)}/lines`, {
        itemNo,
        countedQuantity,
        note: note ?? null,
      }),
    );
  }

  /** Posts the differences as adjustments and closes the count. */
  postStockCount(countNo: string, overrideReason?: string): Promise<StockCountPosted> {
    return firstValueFrom(
      this.http.post<StockCountPosted>(
        `${this.base}/counts/${encodeURIComponent(countNo)}/post`,
        { overrideReason: overrideReason ?? null },
      ),
    );
  }

  /** Abandons a count. It stays on the record. */
  cancelStockCount(countNo: string): Promise<StockCount> {
    return firstValueFrom(
      this.http.post<StockCount>(
        `${this.base}/counts/${encodeURIComponent(countNo)}/cancel`,
        {},
      ),
    );
  }

  /** The locations stock can be held at. */
  /** When each place reorders each item, and how much. */
  reorderPolicies(locationCode?: string, activeOnly = false): Promise<ReorderPolicyRow[]> {
    const params = new URLSearchParams();

    if (locationCode) {
      params.set('locationCode', locationCode);
    }

    if (activeOnly) {
      params.set('activeOnly', 'true');
    }

    const query = params.size > 0 ? `?${params}` : '';

    return firstValueFrom(
      this.http.get<ReorderPolicyRow[]>(`${this.base}/reorder-policies${query}`),
    );
  }

  /** Writes a reorder policy for one item at one place. */
  saveReorderPolicy(request: {
    itemNo: string;
    locationCode: string;
    kind: ReorderKind;
    reorderPoint: number;
    reorderQuantity: number;
    maximumInventory: number;
    minimumOrderQuantity: number;
    orderMultiple: number;
    leadTimeDays: number;
    vendorNo?: string | null;
    isActive: boolean;
  }): Promise<ReorderPolicyRow> {
    return firstValueFrom(
      this.http.put<ReorderPolicyRow>(
        `${this.base}/reorder-policies/${encodeURIComponent(request.itemNo)}/${encodeURIComponent(request.locationCode)}`,
        request,
      ),
    );
  }

  /** Leaves a place with no rule for that item. */
  removeReorderPolicy(itemNo: string, locationCode: string): Promise<void> {
    return firstValueFrom(
      this.http.delete<void>(
        `${this.base}/reorder-policies/${encodeURIComponent(itemNo)}/${encodeURIComponent(locationCode)}`,
      ),
    );
  }

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

  /** Transfers, most recently raised first. */
  transfers(status?: string): Promise<Transfer[]> {
    const params = status ? new HttpParams().set('status', status) : undefined;

    return firstValueFrom(this.http.get<Transfer[]>(`${this.base}/transfers`, { params }));
  }

  /** Raises a transfer. Nothing moves until it is shipped. */
  createTransfer(request: CreateTransferRequest): Promise<TransferCreated> {
    return firstValueFrom(this.http.post<TransferCreated>(`${this.base}/transfers`, request));
  }

  /** Sends the goods: out of the source and into transit. */
  shipTransfer(transferNo: string): Promise<TransferMoveReceipt> {
    return firstValueFrom(
      this.http.post<TransferMoveReceipt>(
        `${this.base}/transfers/${encodeURIComponent(transferNo)}/ship`,
        {},
      ),
    );
  }

  /**
   * Lands the goods: out of transit and into the destination.
   *
   * Shortages are keyed by item and hold what actually arrived. Anything left out is taken as
   * having arrived in full, which is the ordinary case and should not need typing.
   */
  receiveTransfer(
    transferNo: string,
    shortages?: Record<string, number>,
  ): Promise<TransferMoveReceipt> {
    return firstValueFrom(
      this.http.post<TransferMoveReceipt>(
        `${this.base}/transfers/${encodeURIComponent(transferNo)}/receive`,
        { shortages },
      ),
    );
  }

  /**
   * The units this company counts, weighs and measures in.
   *
   * A setup screen asks for the inactive ones too, or the only way to switch one back on is to
   * guess that it is there.
   */
  units(includeInactive = false): Promise<UnitOfMeasure[]> {
    const params = includeInactive ? new HttpParams().set('includeInactive', 'true') : new HttpParams();

    return firstValueFrom(this.http.get<UnitOfMeasure[]>(`${this.base}/units`, { params }));
  }

  /** The units one item may be handled in, base unit first. */
  itemUnits(itemNo: string): Promise<ItemUnit[]> {
    return firstValueFrom(
      this.http.get<ItemUnit[]>(`${this.base}/items/${encodeURIComponent(itemNo)}/units`),
    );
  }

  /** Says what a barcode is and how many it stands for. */
  scan(barcode: string): Promise<ResolvedQuantity> {
    return firstValueFrom(
      this.http.get<ResolvedQuantity>(`${this.base}/scan/${encodeURIComponent(barcode)}`),
    );
  }

  /** Adds a unit to the company's list, or changes one on it. */
  saveUnit(unit: UnitOfMeasure): Promise<UnitOfMeasure> {
    return firstValueFrom(this.http.post<UnitOfMeasure>(`${this.base}/units`, unit));
  }

  /** Says what one of a unit holds, for one item. */
  saveItemUnit(
    itemNo: string,
    unit: { unitCode: string; quantityPerUnit: number; barcode?: string | null },
  ): Promise<ItemUnit> {
    return firstValueFrom(
      this.http.post<ItemUnit>(
        `${this.base}/items/${encodeURIComponent(itemNo)}/units`,
        { ...unit, isActive: true },
      ),
    );
  }

  /** Takes a unit off an item. What already posted keeps the factor it posted with. */
  removeItemUnit(itemNo: string, unitCode: string): Promise<void> {
    return firstValueFrom(
      this.http.delete<void>(
        `${this.base}/items/${encodeURIComponent(itemNo)}/units/${encodeURIComponent(unitCode)}`,
      ),
    );
  }

  /** The bins at a location, in the order a picker walks them. */
  bins(locationCode: string): Promise<Bin[]> {
    return firstValueFrom(
      this.http.get<Bin[]>(`${this.base}/locations/${encodeURIComponent(locationCode)}/bins`),
    );
  }

  /** Goods moved between shelves, most recent first. */
  binMovements(locationCode?: string, take = 50): Promise<BinMovementRow[]> {
    const params = new URLSearchParams({ take: String(take) });

    if (locationCode) {
      params.set('locationCode', locationCode);
    }

    return firstValueFrom(this.http.get<BinMovementRow[]>(`${this.base}/bin-movements?${params}`));
  }

  /** Moves goods between shelves inside one place, all lines or none. */
  postBinMovement(request: {
    locationCode: string;
    lines: { itemNo: string; fromBinCode: string; toBinCode: string; quantity: number }[];
    movementDate?: string | null;
    note?: string | null;
  }): Promise<BinMovementResult> {
    return firstValueFrom(
      this.http.post<BinMovementResult>(`${this.base}/bin-movements`, request),
    );
  }

  /** What is standing on each shelf at a location. */
  binContents(locationCode: string, itemNo?: string): Promise<BinContent[]> {
    const params = itemNo ? new HttpParams().set('itemNo', itemNo) : new HttpParams();

    return firstValueFrom(
      this.http.get<BinContent[]>(
        `${this.base}/locations/${encodeURIComponent(locationCode)}/bin-contents`,
        { params },
      ),
    );
  }

  /** Adds a bin to a location, or changes one already there. */
  saveBin(locationCode: string, bin: Bin): Promise<Bin> {
    return firstValueFrom(
      this.http.post<Bin>(`${this.base}/locations/${encodeURIComponent(locationCode)}/bins`, bin),
    );
  }

  /** Takes an empty bin off a location. */
  removeBin(locationCode: string, binCode: string): Promise<void> {
    return firstValueFrom(
      this.http.delete<void>(
        `${this.base}/locations/${encodeURIComponent(locationCode)}/bins/${encodeURIComponent(binCode)}`,
      ),
    );
  }

  /** Turns bin tracking on or off at a location. */
  setBinTracking(locationCode: string, usesBins: boolean): Promise<{ code: string; usesBins: boolean }> {
    return firstValueFrom(
      this.http.post<{ code: string; usesBins: boolean }>(
        `${this.base}/locations/${encodeURIComponent(locationCode)}/bin-tracking`,
        { usesBins },
      ),
    );
  }

  /** The reasons this company adjusts stock for. */
  adjustmentReasons(includeWithdrawn = false): Promise<AdjustmentReason[]> {
    const params = includeWithdrawn
      ? new HttpParams().set('includeWithdrawn', 'true')
      : new HttpParams();

    return firstValueFrom(
      this.http.get<AdjustmentReason[]>(`${this.base}/adjustment-reasons`, { params }),
    );
  }

  /** Adds a reason, or changes one already there. */
  saveAdjustmentReason(reason: AdjustmentReason): Promise<AdjustmentReason> {
    return firstValueFrom(
      this.http.post<AdjustmentReason>(`${this.base}/adjustment-reasons`, reason),
    );
  }

  /**
   * What was adjusted under each reason, and what it was worth.
   *
   * Adjustments made without a reason come back under a row of their own rather than being
   * dropped, so the report and the ledger agree.
   */
  shrinkage(from: string, to: string, locationCode?: string): Promise<ShrinkageRow[]> {
    let params = new HttpParams().set('from', from).set('to', to);

    if (locationCode) {
      params = params.set('locationCode', locationCode);
    }

    return firstValueFrom(
      this.http.get<ShrinkageRow[]>(`${this.base}/reports/shrinkage`, { params }),
    );
  }

  /** The categories items are grouped under, and the accounts each posts to. */
  itemCategories(): Promise<ItemCategory[]> {
    return firstValueFrom(this.http.get<ItemCategory[]>(`${this.base}/categories`));
  }

  /** Adds a category, or changes one already there. */
  saveItemCategory(category: ItemCategory): Promise<ItemCategory> {
    return firstValueFrom(this.http.post<ItemCategory>(`${this.base}/categories`, category));
  }

  /** Moves an item into a category. What already posted keeps the accounts it posted to. */
  setItemCategory(itemNo: string, categoryCode: string | null): Promise<unknown> {
    return firstValueFrom(
      this.http.post(`${this.base}/items/${encodeURIComponent(itemNo)}/category`, { categoryCode }),
    );
  }

  /**
   * Which categories are not reaching the ledger, and what that has cost so far.
   *
   * A movement under a category with no inventory account posts nothing, on purpose. This is the
   * only thing that says so.
   */
  categoryPostingGaps(): Promise<CategoryPostingGap[]> {
    return firstValueFrom(
      this.http.get<CategoryPostingGap[]>(`${this.base}/reports/posting-gaps`),
    );
  }

  /** The colours, sizes and flavours an item is stocked as. */
  itemVariants(itemNo: string): Promise<ItemVariant[]> {
    return firstValueFrom(
      this.http.get<ItemVariant[]>(`${this.base}/items/${encodeURIComponent(itemNo)}/variants`),
    );
  }

  /** Adds a variant to an item, or changes one already there. */
  saveItemVariant(itemNo: string, variant: ItemVariant): Promise<ItemVariant> {
    return firstValueFrom(
      this.http.post<ItemVariant>(
        `${this.base}/items/${encodeURIComponent(itemNo)}/variants`,
        variant,
      ),
    );
  }

  /**
   * Turns variants on or off for an item.
   *
   * Off is refused while stock still stands under them: those entries would keep pointing at a
   * variant nothing reads, and the item's cost layers would merge colours that were never the
   * same goods.
   */
  setItemHasVariants(itemNo: string, hasVariants: boolean): Promise<unknown> {
    return firstValueFrom(
      this.http.post(`${this.base}/items/${encodeURIComponent(itemNo)}/has-variants`, {
        hasVariants,
      }),
    );
  }

  /** What each variant is holding, by location. */
  variantStock(itemNo: string): Promise<VariantStockRow[]> {
    return firstValueFrom(
      this.http.get<VariantStockRow[]>(
        `${this.base}/items/${encodeURIComponent(itemNo)}/variant-stock`,
      ),
    );
  }

  /**
   * What is on hand, what is promised, and what is left to promise.
   *
   * The third column is the only one anybody can act on. On hand is a fact about the shelf;
   * available is that fact less what has already been promised to somebody else.
   */
  stockAvailable(itemNo?: string, locationCode?: string): Promise<StockAvailabilityRow[]> {
    let params = new HttpParams();

    if (itemNo) {
      params = params.set('itemNo', itemNo);
    }

    if (locationCode) {
      params = params.set('locationCode', locationCode);
    }

    return firstValueFrom(
      this.http.get<StockAvailabilityRow[]>(`${this.base}/stock/available`, { params }),
    );
  }

  /** What stock is being held, and for what. */
  reservations(documentNo?: string, outstandingOnly = true): Promise<StockReservationRow[]> {
    let params = new HttpParams().set('outstandingOnly', outstandingOnly);

    if (documentNo) {
      params = params.set('documentNo', documentNo);
    }

    return firstValueFrom(
      this.http.get<StockReservationRow[]>(`${this.base}/reservations`, { params }),
    );
  }

  /** Holds stock for a document. Moves nothing. */
  reserveStock(request: ReserveStockRequest): Promise<StockReservationRow> {
    return firstValueFrom(
      this.http.post<StockReservationRow>(`${this.base}/reservations`, request),
    );
  }

  /** Lets held stock go, and keeps the record of what was held. */
  releaseStock(documentNo: string, reason?: string): Promise<{ released: number }> {
    return firstValueFrom(
      this.http.post<{ released: number }>(
        `${this.base}/reservations/${encodeURIComponent(documentNo)}/release`,
        { reason },
      ),
    );
  }

  /**
   * What the stock was worth on a day.
   *
   * The same arithmetic that posts to the inventory account, so the two tie by construction
   * rather than by agreement.
   */
  stockValuation(asOf: string, itemNo?: string, locationCode?: string): Promise<ValuationRow[]> {
    let params = new HttpParams().set('asOf', asOf);

    if (itemNo) {
      params = params.set('itemNo', itemNo);
    }

    if (locationCode) {
      params = params.set('locationCode', locationCode);
    }

    return firstValueFrom(
      this.http.get<ValuationRow[]>(`${this.base}/reports/valuation`, { params }),
    );
  }

  /** How long the stock on hand has been sitting, in bands. */
  stockAgeing(asOf: string, itemNo?: string, locationCode?: string): Promise<AgeingRow[]> {
    let params = new HttpParams().set('asOf', asOf);

    if (itemNo) {
      params = params.set('itemNo', itemNo);
    }

    if (locationCode) {
      params = params.set('locationCode', locationCode);
    }

    return firstValueFrom(this.http.get<AgeingRow[]>(`${this.base}/reports/ageing`, { params }));
  }

  /** How fast each item moves, slowest first. */
  stockVelocity(from: string, to: string): Promise<VelocityRow[]> {
    const params = new HttpParams().set('from', from).set('to', to);

    return firstValueFrom(this.http.get<VelocityRow[]>(`${this.base}/reports/velocity`, { params }));
  }
}

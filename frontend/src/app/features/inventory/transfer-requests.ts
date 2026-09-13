import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  Item,
  StockLocation,
  TransferRequestRow,
  TransferRequestStatus,
} from '../../core/api/asap-api.models';
import { AuthService } from '../../core/auth/auth.service';
import { InventoryService } from '../../core/api/inventory.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/** A line being prepared, before the request is raised. */
interface DraftLine {
  itemNo: string;
  quantity: number;
}

/**
 * Branches asking for stock, and whoever holds it answering.
 *
 * The two sides share a screen because they share a document, but they see different buttons:
 * asking and agreeing are separate permissions, and the screen does not offer somebody an action
 * the server will refuse them.
 *
 * On an answer, the quantity to agree is editable and starts at what was asked. It can be lowered
 * and the server refuses raising it, so the box is capped at the request rather than letting
 * somebody type twenty and find out.
 */
@Component({
  selector: 'asap-transfer-requests',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './transfer-requests.html',
})
export class TransferRequests implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(InventoryService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly requests = signal<TransferRequestRow[]>([]);
  protected readonly locations = signal<StockLocation[]>([]);
  protected readonly items = signal<Item[]>([]);
  protected readonly selected = signal<TransferRequestRow | null>(null);
  protected readonly draft = signal<DraftLine[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);

  protected filterStatus: TransferRequestStatus | '' = '';

  protected fromLocation = '';
  protected toLocation = '';
  protected neededBy = '';
  protected reason = '';
  protected newItemNo = '';
  protected newQuantity: number | null = null;

  /** What is being agreed, by line number, while answering. */
  protected agreeing: Record<number, number> = {};
  protected rejectionReason = '';

  async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canAsk(): boolean {
    return this.auth.can('Inventory.TransferRequest.Create');
  }

  protected canAnswer(): boolean {
    return this.auth.can('Inventory.Transfer.Post');
  }

  /** Withdrawable until anything has gone onto a transfer; after that the transfer is where to act. */
  protected canCancel(request: TransferRequestRow): boolean {
    return (
      this.canAsk() &&
      (request.status === 'Draft' || request.status === 'Submitted' || request.status === 'Approved') &&
      !request.lines.some((line) => line.quantityTransferred > 0)
    );
  }

  protected places(): StockLocation[] {
    return this.locations().filter((location) => !location.isInTransit && !location.isBlocked);
  }

  protected statusLabel(status: TransferRequestStatus): string {
    return this.t(`inventory.transferRequests.status.${status}` as TranslationKey);
  }

  protected select(request: TransferRequestRow): void {
    this.selected.set(request);
    this.rejectionReason = '';
    this.agreeing = Object.fromEntries(request.lines.map((line) => [line.lineNo, line.quantityRequested]));
  }

  protected addLine(): void {
    if (!this.newItemNo || !this.newQuantity || this.newQuantity <= 0) {
      return;
    }

    this.draft.update((lines) => [...lines, { itemNo: this.newItemNo, quantity: this.newQuantity! }]);
    this.newItemNo = '';
    this.newQuantity = null;
  }

  protected removeLine(index: number): void {
    this.draft.update((lines) => lines.filter((_, i) => i !== index));
  }

  protected async raise(submit: boolean): Promise<void> {
    if (!this.fromLocation || !this.toLocation || this.draft().length === 0) {
      return;
    }

    await this.act(async () => {
      let request = await this.api.createTransferRequest({
        fromLocationCode: this.fromLocation,
        toLocationCode: this.toLocation,
        lines: this.draft().map((line) => ({ itemNo: line.itemNo, quantity: line.quantity })),
        neededByDate: this.neededBy || null,
        reason: this.reason || null,
      });

      if (submit) {
        request = await this.api.submitTransferRequest(request.no);
      }

      this.draft.set([]);
      this.reason = '';
      this.neededBy = '';

      this.messages.showSuccess(
        this.t(submit ? 'inventory.transferRequests.sent' : 'inventory.transferRequests.saved', {
          No: request.no,
        }),
      );

      return request;
    });
  }

  protected async submit(request: TransferRequestRow): Promise<void> {
    await this.act(() => this.api.submitTransferRequest(request.no));
  }

  protected async approve(request: TransferRequestRow): Promise<void> {
    await this.act(async () => {
      const answered = await this.api.approveTransferRequest(
        request.no,
        request.lines.map((line) => ({
          lineNo: line.lineNo,
          quantity: Math.min(Number(this.agreeing[line.lineNo] ?? 0), line.quantityRequested),
        })),
      );

      this.messages.showSuccess(this.t('inventory.transferRequests.agreed.done', { No: request.no }));

      return answered;
    });
  }

  protected async reject(request: TransferRequestRow): Promise<void> {
    if (!this.rejectionReason.trim()) {
      return;
    }

    await this.act(() => this.api.rejectTransferRequest(request.no, this.rejectionReason.trim()));
  }

  protected async transfer(request: TransferRequestRow): Promise<void> {
    this.busy.set(true);

    try {
      const sent = await this.api.transferFromRequest(request.no);

      this.messages.showSuccess(
        this.t('inventory.transferRequests.transferred', { No: request.no, TransferNo: sent.transferNo }),
      );

      await this.reload(request.no);
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async cancel(request: TransferRequestRow): Promise<void> {
    await this.act(() => this.api.cancelTransferRequest(request.no));
  }

  protected async filter(): Promise<void> {
    await this.reload();
  }

  private async act(work: () => Promise<TransferRequestRow>): Promise<void> {
    this.busy.set(true);

    try {
      const result = await work();

      await this.reload(result.no);
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  private async reload(keepNo?: string): Promise<void> {
    this.loading.set(true);

    try {
      const [requests, locations, items] = await Promise.all([
        this.api.transferRequests(this.filterStatus || undefined),
        this.locations().length > 0 ? Promise.resolve(this.locations()) : this.api.locations(),
        this.items().length > 0 ? Promise.resolve(this.items()) : this.api.items(),
      ]);

      this.requests.set(requests);
      this.locations.set(locations);
      this.items.set(items.filter((item) => !item.isBlocked));

      const still = requests.find((request) => request.no === (keepNo ?? this.selected()?.no));

      if (still) {
        this.select(still);
      } else {
        this.selected.set(null);
      }
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

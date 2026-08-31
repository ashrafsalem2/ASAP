import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CreateRequisitionRequest, Requisition } from '../../core/api/asap-api.models';
import { AuthService } from '../../core/auth/auth.service';
import { PurchasingService } from '../../core/api/purchasing.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/** One line being typed onto a new requisition. */
interface DraftLine {
  no: string;
  description: string;
  quantity: number | null;
  estimatedUnitCost: number | null;
  suggestedVendorNo: string;
}

/** What is being ordered from one vendor, keyed per line. */
interface OrderLine {
  quantity: number | null;
  price: number | null;
}

/**
 * Requests for things to be bought.
 *
 * A requisition names a need rather than a purchase, so nothing here posts and nothing commits the
 * company. The two rules worth understanding are both refusals: nobody signs for their own
 * request, and no line can be ordered past what was asked for. One requisition becomes as many
 * orders as it has vendors, which is why each line counts what has already gone.
 */
@Component({
  selector: 'asap-requisitions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './requisitions.html',
})
export class Requisitions implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(PurchasingService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly requisitions = signal<Requisition[]>([]);
  protected readonly selected = signal<Requisition | null>(null);
  protected readonly drafting = signal(false);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);

  protected statusFilter = '';
  protected locationCode = '';
  protected neededBy = '';
  protected description = '';
  protected justification = '';
  protected reason = '';
  protected orderVendorNo = '';

  protected draftLines: DraftLine[] = [this.blankLine()];

  private readonly orderEntries = new Map<number, OrderLine>();

  async ngOnInit(): Promise<void> {
    try {
      await this.refresh();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected canAsk(): boolean {
    return this.auth.can('Purchasing.Requisition.Create');
  }

  protected canApprove(): boolean {
    return this.auth.can('Purchasing.Requisition.Approve');
  }

  protected canOrder(): boolean {
    return this.auth.can('Purchasing.Order.Create');
  }

  protected async refresh(): Promise<void> {
    this.requisitions.set(await this.api.requisitions(this.statusFilter || undefined));
  }

  protected startNew(): void {
    this.drafting.set(true);
    this.selected.set(null);
    this.locationCode = '';
    this.neededBy = '';
    this.description = '';
    this.justification = '';
    this.draftLines = [this.blankLine()];
  }

  protected cancelDraft(): void {
    this.drafting.set(false);
  }

  protected addLine(): void {
    this.draftLines = [...this.draftLines, this.blankLine()];
  }

  protected removeLine(index: number): void {
    this.draftLines = this.draftLines.filter((_, at) => at !== index);
  }

  protected trackLine(index: number): number {
    return index;
  }

  /** What the draft is estimated at, which is what decides whether it needs signing for. */
  protected draftEstimate(): number {
    return this.draftLines.reduce(
      (total, line) => total + (line.quantity ?? 0) * (line.estimatedUnitCost ?? 0),
      0,
    );
  }

  protected async save(): Promise<void> {
    const lines = this.draftLines
      .filter((line) => line.no.trim() && (line.quantity ?? 0) > 0)
      .map((line) => ({
        type: 'Item',
        no: line.no.trim(),
        quantity: line.quantity ?? 0,
        estimatedUnitCost: line.estimatedUnitCost ?? 0,
        description: line.description.trim() || null,
        suggestedVendorNo: line.suggestedVendorNo.trim() || null,
      }));

    if (this.busy() || lines.length === 0) {
      return;
    }

    const request: CreateRequisitionRequest = {
      lines,
      locationCode: this.locationCode || null,
      neededByDate: this.neededBy || null,
      description: this.description || null,
      justification: this.justification || null,
    };

    await this.run('save', async () => {
      const created = await this.api.createRequisition(request);

      this.messages.showSuccess(
        this.t('purchasing.requisitions.saved', {
          No: created.no,
          Amount: this.i18n.total(created.estimatedAmount),
        }),
      );

      this.drafting.set(false);
      this.selected.set(created);
    });
  }

  protected async open(requisition: Requisition): Promise<void> {
    this.drafting.set(false);
    this.reason = '';
    this.orderEntries.clear();

    try {
      this.selected.set(await this.api.requisition(requisition.no));
    } catch (error) {
      this.messages.showError(error);
    }
  }

  protected async submit(): Promise<void> {
    await this.act('submit', (no) => this.api.submitRequisition(no));
  }

  protected async approve(): Promise<void> {
    await this.act('approve', (no) => this.api.approveRequisition(no));
  }

  protected async reject(): Promise<void> {
    await this.act('reject', (no) => this.api.rejectRequisition(no, this.reason.trim() || undefined));
  }

  protected async cancel(): Promise<void> {
    await this.act('cancel', (no) => this.api.cancelRequisition(no, this.reason.trim() || undefined));
  }

  protected orderQuantityOf(lineNo: number): number | null {
    return this.orderEntries.get(lineNo)?.quantity ?? null;
  }

  protected setOrderQuantity(lineNo: number, value: number | null): void {
    const entry = this.orderEntries.get(lineNo) ?? { quantity: null, price: null };
    this.orderEntries.set(lineNo, { ...entry, quantity: value });
  }

  protected orderPriceOf(lineNo: number): number | null {
    return this.orderEntries.get(lineNo)?.price ?? null;
  }

  protected setOrderPrice(lineNo: number, value: number | null): void {
    const entry = this.orderEntries.get(lineNo) ?? { quantity: null, price: null };
    this.orderEntries.set(lineNo, { ...entry, price: value });
  }

  /**
   * Raises an order for one vendor from the lines that were keyed.
   *
   * The prices come from this screen rather than from the requisition, because the requisition
   * carried a guess and an order posts real money.
   */
  protected async raiseOrder(): Promise<void> {
    const requisition = this.selected();

    if (!requisition || this.busy() || !this.orderVendorNo.trim()) {
      return;
    }

    const lines = requisition.lines
      .map((line) => ({ line, entry: this.orderEntries.get(line.lineNo) }))
      .filter((row) => (row.entry?.quantity ?? 0) > 0)
      .map((row) => ({
        lineNo: row.line.lineNo,
        quantity: row.entry!.quantity!,
        directUnitCost: row.entry!.price ?? row.line.estimatedUnitCost,
      }));

    if (lines.length === 0) {
      return;
    }

    await this.run('order', async () => {
      const result = await this.api.orderFromRequisition(
        requisition.no,
        this.orderVendorNo.trim(),
        lines,
      );

      this.messages.showSuccess(
        this.t('purchasing.requisitions.ordered.done', {
          No: result.order.no,
          Vendor: this.orderVendorNo.trim(),
        }),
      );

      this.orderEntries.clear();
      this.orderVendorNo = '';
      this.selected.set(await this.api.requisition(requisition.no));
    });
  }

  protected statusLabel(status: string): string {
    return this.t(`purchasing.requisitions.status.${status}` as TranslationKey);
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  private blankLine(): DraftLine {
    return { no: '', description: '', quantity: null, estimatedUnitCost: null, suggestedVendorNo: '' };
  }

  private async act(what: string, run: (no: string) => Promise<Requisition>): Promise<void> {
    const requisition = this.selected();

    if (!requisition) {
      return;
    }

    await this.run(what, async () => {
      this.selected.set(await run(requisition.no));
    });
  }

  private async run(what: string, work: () => Promise<void>): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.busy.set(what);
    this.messages.clear();

    try {
      await work();
      await this.refresh();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(null);
    }
  }
}

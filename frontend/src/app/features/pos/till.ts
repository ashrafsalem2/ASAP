import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  Item,
  ParkedSale,
  PosSession,
  Party,
  PosReceiptPosted,
  PosStation,
  TaxCodeSummary,
  TenderKind,
} from '../../core/api/asap-api.models';
import { AsapMessage } from '../../core/api/asap-message';
import { FinanceService } from '../../core/api/finance.service';
import { InventoryService } from '../../core/api/inventory.service';
import { PosLinePayload, PosService, PosTenderPayload } from '../../core/api/pos.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/** One line on the sale being rung up. */
interface TillLine {
  itemNo: string;
  description: string;
  quantity: number;
  unitPrice: number;
  discountPercent: number;
  taxCode: string;
  taxPercent: number;
}

/** Money offered towards it. */
interface TillTender {
  kind: TenderKind;
  amount: number;
  reference: string;
}

/**
 * The till.
 *
 * The one screen in the system used with a queue waiting, which decides everything about it. The
 * running total is always visible, the change due is worked out as the money is keyed rather than
 * after, and nothing that would refuse is offered — a cashier discovering at the moment of
 * payment that this till cannot give change from a card has already lost the room.
 */
@Component({
  selector: 'asap-till',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './till.html',
  styleUrl: './pos.scss',
})
export class Till implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly pos = inject(PosService);
  private readonly inventory = inject(InventoryService);
  private readonly finance = inject(FinanceService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly stations = signal<PosStation[]>([]);
  protected readonly items = signal<Item[]>([]);
  protected readonly taxCodes = signal<TaxCodeSummary[]>([]);
  protected readonly customers = signal<Party[]>([]);
  protected readonly session = signal<PosSession | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);

  /** The sale being rung up, held as signals so the totals recompute as it is keyed. */
  protected readonly lines = signal<TillLine[]>([]);
  protected readonly tenders = signal<TillTender[]>([]);
  protected readonly parked = signal<ParkedSale[]>([]);

  /** The basket this sale came from, so paying for it closes that basket rather than orphaning it. */
  protected readonly recalledFrom = signal<string | null>(null);

  protected stationCode = '';
  protected openingFloat: number | null = null;
  protected declaredCash: number | null = null;
  protected scanned = '';
  protected scannedQuantity: number | null = null;
  protected scannedDiscount: number | null = null;
  protected parkedAs = '';
  protected returnsReceiptNo = '';

  /**
   * Who the sale is for, or blank for whoever walks in.
   *
   * Blank is the ordinary case and stays the default: naming a customer at a shop counter slows
   * every sale down, and most sales have nobody to name. It matters when there is one, because a
   * customer's group is what decides whether an offer limited to staff or to trade applies at
   * all. Without this field those offers read as configured on the offer screen and discount
   * nobody, which is worse than not having them.
   */
  protected customerNo = '';

  /**
   * Whether the till is taking goods back rather than selling them.
   *
   * A mode rather than a negative quantity typed by hand. Asking a cashier to key a minus sign
   * under pressure is asking for a sale of minus three to be rung up by accident, and the
   * arithmetic of that mistake is the shop paying somebody to take stock away.
   */
  protected returning = false;

  protected readonly net = computed(() =>
    round(this.lines().reduce((total, line) => total + lineAmount(line), 0)),
  );

  protected readonly discount = computed(() =>
    round(
      this.lines().reduce(
        (total, line) => total + line.quantity * line.unitPrice * (line.discountPercent / 100),
        0,
      ),
    ),
  );

  /**
   * Tax worked out per line, at the rate that line carries.
   *
   * Computed here and again on the server, which is not duplication for its own sake: the till
   * has to show a total before it asks for money, and a screen that could only learn the total by
   * asking would show the customer a figure that arrives after they have handed over a note.
   */
  protected readonly tax = computed(() =>
    round(
      this.lines().reduce(
        (total, line) => total + round(lineAmount(line) * (line.taxPercent / 100)),
        0,
      ),
    ),
  );

  protected readonly total = computed(() => round(this.net() + this.tax()));

  protected readonly tendered = computed(() =>
    round(this.tenders().reduce((sum, tender) => sum + tender.amount, 0)),
  );

  /** Negative while there is still something to pay, positive once there is change to hand back. */
  protected readonly change = computed(() => round(this.tendered() - this.total()));

  protected readonly outstanding = computed(() => round(Math.max(-this.change(), 0)));

  /** Only notes and coins can come back, so change is only offerable against cash taken. */
  protected readonly cashOffered = computed(() =>
    round(
      this.tenders()
        .filter((tender) => tender.kind === 'Cash')
        .reduce((sum, tender) => sum + tender.amount, 0),
    ),
  );

  /** True once anything on the sale is going back rather than out. */
  protected readonly isRefund = computed(() => this.total() < 0);

  /**
   * A refund read the way a cashier counts it: what is owed, and what has gone back, both
   * positive.
   *
   * Negating in the template printed "-0.00" before anything had been handed over, which on a
   * till reads as a fault rather than as nothing.
   */
  protected readonly toHandBack = computed(() => round(-this.total()));

  protected readonly handedBack = computed(() => round(-this.tendered()));

  protected readonly canTakePayment = computed(() => {
    if (this.lines().length === 0) {
      return false;
    }

    // A refund is paid out exactly. There is no change on money going the other way, so the
    // only acceptable state is that what is handed back matches what is owed.
    if (this.isRefund()) {
      return this.change() === 0;
    }

    return this.outstanding() === 0 && this.change() <= this.cashOffered();
  });

  async ngOnInit(): Promise<void> {
    try {
      const [stations, items, taxCodes, customers] = await Promise.all([
        this.pos.stations(),
        this.inventory.items(),
        this.finance.taxCodes(),
        this.finance.parties('Customer'),
      ]);

      this.stations.set(stations.filter((station) => !station.isBlocked));
      this.items.set(items.filter((item) => !item.isBlocked));
      this.taxCodes.set(taxCodes);
      this.customers.set(customers.filter((customer) => !customer.isBlocked));

      // Straight back to whichever till is already trading. A cashier returning from a break
      // should not have to remember which one they were on.
      const open = this.stations().find((station) => station.openSessionNo);

      if (open) {
        this.stationCode = open.code;
        await this.loadSession(open.openSessionNo!);
      } else if (this.stations().length === 1) {
        this.stationCode = this.stations()[0].code;
      }
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canOpen(): boolean {
    return this.auth.can('Pos.Session.Create');
  }

  protected canClose(): boolean {
    return this.auth.can('Pos.Session.Post');
  }

  protected canSell(): boolean {
    return this.auth.can('Pos.Receipt.Post');
  }

  /**
   * What a tender is called.
   *
   * The key is built from the kind, so the type has to be asserted. Worth it: the alternative is
   * four near-identical branches that drift apart the first time a fifth tender is added.
   */
  protected tenderLabel(kind: TenderKind): string {
    return this.t(`pos.tender.${kind}` as TranslationKey);
  }

  protected quantity(value: number): string {
    return new Intl.NumberFormat(this.i18n.locale(), { maximumFractionDigits: 5 }).format(value);
  }

  protected itemLabel(item: Item): string {
    const description =
      this.i18n.language() === 'ar' && item.descriptionArabic
        ? item.descriptionArabic
        : item.description;

    return `${item.no} — ${description}`;
  }

  protected lineAmountOf(line: TillLine): number {
    return lineAmount(line);
  }

  /** Adds what was scanned to the sale, or bumps the line that is already there. */
  protected addScanned(): void {
    const item = this.items().find((candidate) => candidate.no === this.scanned);

    if (!item) {
      return;
    }

    const keyed = this.scannedQuantity ?? 1;
    const quantity = this.returning ? -Math.abs(keyed) : Math.abs(keyed);
    const discountPercent = this.scannedDiscount ?? 0;
    const taxCode = this.taxCodes()[0]?.code ?? '';
    const taxPercent = this.taxCodes()[0]?.percentage ?? 0;

    const existing = this.lines().findIndex(
      (line) => line.itemNo === item.no && line.discountPercent === discountPercent,
    );

    if (existing >= 0) {
      // Scanning the same thing twice means two of them, which is what a person at a counter
      // means by scanning it twice. A second line saying the same thing would print a receipt
      // nobody can read.
      this.lines.update((lines) =>
        lines.map((line, index) =>
          index === existing ? { ...line, quantity: line.quantity + quantity } : line,
        ),
      );
    } else {
      this.lines.update((lines) => [
        ...lines,
        {
          itemNo: item.no,
          description:
            this.i18n.language() === 'ar' && item.descriptionArabic
              ? item.descriptionArabic
              : item.description,
          quantity,
          unitPrice: item.unitPrice,
          discountPercent,
          taxCode,
          taxPercent,
        },
      ]);
    }

    this.scanned = '';
    this.scannedQuantity = null;
    this.scannedDiscount = null;
  }

  protected removeLine(index: number): void {
    this.lines.update((lines) => lines.filter((_, position) => position !== index));
  }

  protected setQuantity(index: number, value: number): void {
    this.lines.update((lines) =>
      lines.map((line, position) => (position === index ? { ...line, quantity: value } : line)),
    );
  }

  /**
   * Offers the exact amount outstanding, which is what most customers hand over.
   *
   * On a refund that is the whole total, negative: the drawer is paying out, and a cashier
   * should not have to key a minus sign to say so.
   */
  protected addTender(kind: TenderKind): void {
    const amount = this.isRefund() ? this.total() : Math.max(this.outstanding(), 0);

    this.tenders.update((tenders) => [...tenders, { kind, amount: round(amount), reference: '' }]);
  }

  protected setTenderAmount(index: number, value: number): void {
    this.tenders.update((tenders) =>
      tenders.map((tender, position) =>
        position === index ? { ...tender, amount: value ?? 0 } : tender,
      ),
    );
  }

  protected removeTender(index: number): void {
    this.tenders.update((tenders) => tenders.filter((_, position) => position !== index));
  }

  /** What to tell the cashier about the sale that just posted. */
  private receiptSummary(posted: PosReceiptPosted): string {
    if (posted.promotionAmount > 0) {
      return this.t('pos.receipt.saved', {
        No: posted.receiptNo,
        Total: this.i18n.total(posted.totalAmount),
        Saved: this.i18n.total(posted.promotionAmount),
      });
    }

    return posted.changeGiven > 0
      ? this.t('pos.receipt.doneWithChange', {
          No: posted.receiptNo,
          Total: this.i18n.total(posted.totalAmount),
          Change: this.i18n.total(posted.changeGiven),
        })
      : this.t('pos.receipt.done', {
          No: posted.receiptNo,
          Total: this.i18n.total(posted.totalAmount),
        });
  }

  protected clearSale(): void {
    this.lines.set([]);
    this.tenders.set([]);
    this.recalledFrom.set(null);
    this.returnsReceiptNo = '';
    this.customerNo = '';
    this.returning = false;
    this.messages.clear();
  }

  /** Sets the sale aside. Nothing posts and nothing is reserved. */
  protected async park(): Promise<void> {
    const session = this.session();

    if (this.busy() || !session || this.lines().length === 0) {
      return;
    }

    this.messages.clear();
    this.busy.set('park');

    try {
      const saved = await this.pos.park(session.no, this.payloadLines(), this.parkedAs || undefined);

      this.messages.showSuccess(
        this.t('pos.park.done', { No: saved.parkedAs || saved.no, Amount: this.i18n.total(saved.netAmount) }),
      );

      this.parkedAs = '';
      this.clearSale();

      await this.loadParked(session.no);
    } catch (error) {
      this.messages.showError(error, this.t('pos.park.action'));
    } finally {
      this.busy.set(null);
    }
  }

  /** Brings a set-aside sale back to the screen exactly as it was left. */
  protected async recall(sale: ParkedSale): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.messages.clear();

    try {
      const recalled = await this.pos.recall(sale.no);

      this.lines.set(
        recalled.lines.map((line) => ({
          itemNo: line.no,
          description: line.description ?? line.no,
          quantity: line.quantity,
          unitPrice: line.unitPrice,
          discountPercent: line.discountPercent,
          taxCode: line.taxCode ?? '',
          taxPercent: this.taxCodes().find((code) => code.code === line.taxCode)?.percentage ?? 0,
        })),
      );

      this.tenders.set([]);
      this.recalledFrom.set(recalled.no);
    } catch (error) {
      this.messages.showError(error, this.t('pos.park.recall'));
    }
  }

  /** Throws a set-aside sale away. It is voided, not deleted. */
  protected async voidParked(sale: ParkedSale): Promise<void> {
    const session = this.session();

    if (this.busy() || !session) {
      return;
    }

    this.messages.clear();

    try {
      await this.pos.voidParked(sale.no);

      if (this.recalledFrom() === sale.no) {
        this.clearSale();
      }

      await this.loadParked(session.no);
    } catch (error) {
      this.messages.showError(error, this.t('pos.park.void'));
    }
  }

  protected async open(): Promise<void> {
    if (this.busy() || !this.stationCode) {
      return;
    }

    this.messages.clear();
    this.busy.set('open');

    try {
      const session = await this.pos.open(this.stationCode, this.openingFloat ?? 0);

      this.session.set(session);
      this.openingFloat = null;
      this.messages.showSuccess(this.t('pos.session.openedNow', { No: session.no }));

      await this.refreshStations();
    } catch (error) {
      this.messages.showError(error, this.t('pos.session.open'));
    } finally {
      this.busy.set(null);
    }
  }

  protected async takePayment(): Promise<void> {
    const session = this.session();

    if (this.busy() || !session || !this.canTakePayment()) {
      return;
    }

    this.messages.clear();
    this.busy.set('pay');

    try {
      const tenders: PosTenderPayload[] = this.tenders().map((tender) => ({
        kind: tender.kind,
        amount: tender.amount,
        reference: tender.reference || undefined,
      }));

      const posted = await this.pos.postReceipt(session.no, this.payloadLines(), tenders, {
        customerNo: this.customerNo || undefined,
        returnsReceiptNo: this.returnsReceiptNo || undefined,
        parkedReceiptNo: this.recalledFrom() ?? undefined,
      });

      this.report(posted.messages);

      // What the offers took off is worth saying out loud. Offers are decided on the server, so
      // the screen the customer was looking at showed the ordinary price and they then paid less
      // than it. Without a line naming the difference, a cashier asked why has nothing to point at.
      this.messages.showSuccess(this.receiptSummary(posted));

      this.clearSale();

      await this.loadSession(session.no);
      await this.loadParked(session.no);
    } catch (error) {
      this.messages.showError(error, this.t('pos.receipt.take'));
    } finally {
      this.busy.set(null);
    }
  }

  protected async read(): Promise<void> {
    const session = this.session();

    if (this.busy() || !session) {
      return;
    }

    this.messages.clear();
    this.busy.set('read');

    try {
      const reading = await this.pos.reading(session.no);

      this.messages.showSuccess(
        this.t('pos.session.reading', {
          No: reading.readingNo,
          Receipts: reading.receiptCount,
          Gross: this.i18n.total(reading.netSales + reading.taxAmount),
          Expected: this.i18n.total(reading.expectedCash),
        }),
      );

      await this.loadSession(session.no);
    } catch (error) {
      this.messages.showError(error, this.t('pos.session.read'));
    } finally {
      this.busy.set(null);
    }
  }

  protected async close(): Promise<void> {
    const session = this.session();

    if (this.busy() || !session || this.declaredCash === null) {
      return;
    }

    this.messages.clear();
    this.busy.set('close');

    try {
      const closed = await this.pos.close(session.no, this.declaredCash);

      this.report(closed.messages);
      this.messages.showSuccess(
        closed.variance === 0
          ? this.t('pos.session.closedExact', {
              No: closed.sessionNo,
              Cash: this.i18n.total(closed.declaredCash),
            })
          : this.t(closed.variance < 0 ? 'pos.session.closedShort' : 'pos.session.closedOver', {
              No: closed.sessionNo,
              Amount: this.i18n.total(Math.abs(closed.variance)),
            }),
      );

      this.session.set(null);
      this.declaredCash = null;
      this.lines.set([]);
      this.tenders.set([]);

      await this.refreshStations();
    } catch (error) {
      this.messages.showError(error, this.t('pos.session.close'));
    } finally {
      this.busy.set(null);
    }
  }

  /**
   * Shows what the server said, less its own confirmation of success.
   *
   * The ledger's "5 entries posted as transaction 81" is true and is not what somebody selling a
   * bottle of water needs to read. Warnings and above always show: selling below cost and taking
   * stock that is not there are exactly what a cashier is meant to see.
   */
  private report(messages: AsapMessage[] | undefined): void {
    this.messages.showAll((messages ?? []).filter((message) => message.severity !== 'Success'));
  }

  private payloadLines(): PosLinePayload[] {
    return this.lines().map((line) => ({
      type: 'Item',
      no: line.itemNo,
      quantity: line.quantity,
      unitPrice: line.unitPrice,
      discountPercent: line.discountPercent,
      taxCode: line.taxCode || undefined,
    }));
  }

  private async loadParked(sessionNo: string): Promise<void> {
    this.parked.set(await this.pos.parked(sessionNo));
  }

  private async refreshStations(): Promise<void> {
    this.stations.set((await this.pos.stations()).filter((station) => !station.isBlocked));
  }

  private async loadSession(sessionNo: string): Promise<void> {
    const detail = await this.pos.session(sessionNo);

    this.session.set(detail.session);
    await this.loadParked(sessionNo);
  }
}

function lineAmount(line: TillLine): number {
  return round(line.quantity * line.unitPrice * (1 - line.discountPercent / 100));
}

function round(value: number): number {
  const rounded = Math.round((value + Number.EPSILON) * 100) / 100;

  // Negative zero prints as "-0.00", which on a till reads as a fault rather than as nothing.
  // It arises the moment a refund total is negated before anything has been handed back.
  return rounded === 0 ? 0 : rounded;
}

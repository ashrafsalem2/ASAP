import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  Item,
  PosSession,
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
  protected readonly session = signal<PosSession | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);

  /** The sale being rung up, held as signals so the totals recompute as it is keyed. */
  protected readonly lines = signal<TillLine[]>([]);
  protected readonly tenders = signal<TillTender[]>([]);

  protected stationCode = '';
  protected openingFloat: number | null = null;
  protected declaredCash: number | null = null;
  protected scanned = '';
  protected scannedQuantity: number | null = null;
  protected scannedDiscount: number | null = null;

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

  protected readonly canTakePayment = computed(
    () =>
      this.lines().length > 0 &&
      this.outstanding() === 0 &&
      this.change() <= this.cashOffered(),
  );

  async ngOnInit(): Promise<void> {
    try {
      const [stations, items, taxCodes] = await Promise.all([
        this.pos.stations(),
        this.inventory.items(),
        this.finance.taxCodes(),
      ]);

      this.stations.set(stations.filter((station) => !station.isBlocked));
      this.items.set(items.filter((item) => !item.isBlocked));
      this.taxCodes.set(taxCodes);

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

    const quantity = this.scannedQuantity ?? 1;
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

  /** Offers the exact amount outstanding, which is what most customers hand over. */
  protected addTender(kind: TenderKind): void {
    this.tenders.update((tenders) => [
      ...tenders,
      { kind, amount: round(Math.max(this.outstanding(), 0)), reference: '' },
    ]);
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

  protected clearSale(): void {
    this.lines.set([]);
    this.tenders.set([]);
    this.messages.clear();
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
      const lines: PosLinePayload[] = this.lines().map((line) => ({
        type: 'Item',
        no: line.itemNo,
        quantity: line.quantity,
        unitPrice: line.unitPrice,
        discountPercent: line.discountPercent,
        taxCode: line.taxCode || undefined,
      }));

      const tenders: PosTenderPayload[] = this.tenders().map((tender) => ({
        kind: tender.kind,
        amount: tender.amount,
        reference: tender.reference || undefined,
      }));

      const posted = await this.pos.postReceipt(session.no, lines, tenders);

      this.report(posted.messages);
      this.messages.showSuccess(
        posted.changeGiven > 0
          ? this.t('pos.receipt.doneWithChange', {
              No: posted.receiptNo,
              Total: this.i18n.total(posted.totalAmount),
              Change: this.i18n.total(posted.changeGiven),
            })
          : this.t('pos.receipt.done', {
              No: posted.receiptNo,
              Total: this.i18n.total(posted.totalAmount),
            }),
      );

      this.lines.set([]);
      this.tenders.set([]);

      await this.loadSession(session.no);
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

  private async refreshStations(): Promise<void> {
    this.stations.set((await this.pos.stations()).filter((station) => !station.isBlocked));
  }

  private async loadSession(sessionNo: string): Promise<void> {
    const detail = await this.pos.session(sessionNo);

    this.session.set(detail.session);
  }
}

function lineAmount(line: TillLine): number {
  return round(line.quantity * line.unitPrice * (1 - line.discountPercent / 100));
}

function round(value: number): number {
  return Math.round((value + Number.EPSILON) * 100) / 100;
}

import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { GlAccount, Party } from '../../core/api/asap-api.models';
import { FinanceService } from '../../core/api/finance.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/** One row of the entry grid. Debit and credit are separate inputs, as an accountant expects. */
interface JournalRow {
  id: number;

  /**
   * Whether the row posts to an account, a customer or a vendor. On the row rather than the
   * journal, so one batch can hold an invoice and its contra -- which is how anybody actually
   * keys a purchase day book.
   */
  accountType: JournalAccountType;

  /** The account, customer or vendor number, depending on the type above. */
  accountNo: string;
  description: string;

  /** The other side's reference, kept on the party entry. Only meaningful on a party row. */
  externalDocumentNo: string;
  debit: number | null;
  credit: number | null;
}

/** What a journal row posts to. Mirrors the server's enum. */
type JournalAccountType = 'GlAccount' | 'Customer' | 'Vendor';

@Component({
  selector: 'asap-journal',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './journal.html',
  styleUrl: './journal.scss',
})
export class Journal implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly finance = inject(FinanceService);
  private readonly messages = inject(MessageService);

  private nextRowId = 1;

  protected readonly accounts = signal<GlAccount[]>([]);
  protected readonly customers = signal<Party[]>([]);
  protected readonly vendors = signal<Party[]>([]);
  protected readonly rows = signal<JournalRow[]>([]);
  protected readonly posting = signal(false);
  protected documentNo = '';
  protected description = '';

  /** Only accounts an entry can actually land on. Headings and totals are not choices. */
  protected readonly postableAccounts = computed(() =>
    this.accounts().filter((account) => account.accountType === 'Posting' && !account.isBlocked),
  );

  protected readonly totalDebit = computed(() =>
    this.rows().reduce((sum, row) => sum + (row.debit ?? 0), 0),
  );

  protected readonly totalCredit = computed(() =>
    this.rows().reduce((sum, row) => sum + (row.credit ?? 0), 0),
  );

  /**
   * How far out the journal is, rounded to the currency's decimals.
   *
   * Rounded before comparing, exactly as the server does, so the screen and the server agree on
   * whether a journal balances. Comparing raw floats here would occasionally show a difference of
   * 0.0000000001 and tell the user something is wrong when nothing is.
   */
  protected readonly difference = computed(
    () => Math.round((this.totalDebit() - this.totalCredit()) * 100) / 100,
  );

  protected readonly isBalanced = computed(() => this.difference() === 0 && this.totalDebit() > 0);

  async ngOnInit(): Promise<void> {
    try {
      const [accounts, customers, vendors] = await Promise.all([
        this.finance.accounts(),
        this.finance.parties('Customer'),
        this.finance.parties('Vendor'),
      ]);

      this.accounts.set(accounts);

      // Blocked parties are left out rather than shown and refused. Offering a choice that will
      // be rejected wastes somebody's time twice.
      this.customers.set(customers.filter((party) => !party.isBlocked));
      this.vendors.set(vendors.filter((party) => !party.isBlocked));
    } catch (error) {
      this.messages.showError(error);
    }

    // Two rows to begin with, because the smallest useful journal has two sides.
    this.addRow();
    this.addRow();
  }

  protected t(key: TranslationKey): string {
    return this.i18n.translate(key);
  }

  protected nameOf(account: GlAccount): string {
    const name =
      this.i18n.language() === 'ar' && account.nameArabic ? account.nameArabic : account.name;

    return `${account.no} — ${name}`;
  }

  /** What a row of this type can post to, so the second dropdown only ever offers valid choices. */
  protected choicesFor(row: JournalRow): { value: string; label: string }[] {
    if (row.accountType === 'Customer') {
      return this.customers().map((party) => ({ value: party.no, label: this.partyLabel(party) }));
    }

    if (row.accountType === 'Vendor') {
      return this.vendors().map((party) => ({ value: party.no, label: this.partyLabel(party) }));
    }

    return this.postableAccounts().map((account) => ({
      value: account.no,
      label: this.nameOf(account),
    }));
  }

  protected isParty(row: JournalRow): boolean {
    return row.accountType !== 'GlAccount';
  }

  private partyLabel(party: Party): string {
    const name = this.i18n.language() === 'ar' && party.nameArabic ? party.nameArabic : party.name;

    return `${party.no} — ${name}`;
  }

  /**
   * Changes what a row posts to, and clears what it was pointing at.
   *
   * Keeping the number would leave a customer number selected against the account list, which
   * looks like a valid row and posts to something else entirely.
   */
  protected changeType(id: number, accountType: JournalAccountType): void {
    this.update(id, { accountType, accountNo: '' });
  }

  protected addRow(): void {
    this.rows.update((rows) => [
      ...rows,
      {
        id: this.nextRowId++,
        accountType: 'GlAccount',
        accountNo: '',
        description: '',
        externalDocumentNo: '',
        debit: null,
        credit: null,
      },
    ]);
  }

  protected removeRow(id: number): void {
    this.rows.update((rows) => rows.filter((row) => row.id !== id));
  }

  protected update(id: number, patch: Partial<JournalRow>): void {
    this.rows.update((rows) => rows.map((row) => (row.id === id ? { ...row, ...patch } : row)));
  }

  /**
   * Keeps debit and credit mutually exclusive.
   *
   * A line holding both is a state nothing can interpret: the domain carries one signed amount, so
   * the grid must resolve which side is meant before it gets there. Typing in one column clears
   * the other, which is also what an accountant expects a ledger sheet to do.
   */
  protected setDebit(id: number, value: string): void {
    const amount = this.parse(value);
    this.update(id, { debit: amount, credit: amount === null ? this.rowOf(id)?.credit ?? null : null });
  }

  protected setCredit(id: number, value: string): void {
    const amount = this.parse(value);
    this.update(id, { credit: amount, debit: amount === null ? this.rowOf(id)?.debit ?? null : null });
  }

  protected async post(): Promise<void> {
    if (this.posting()) {
      return;
    }

    const lines = this.rows()
      .filter((row) => row.accountNo && (row.debit || row.credit))
      .map((row) => ({
        accountNo: row.accountNo,
        accountType: row.accountType,

        // The domain carries one signed amount: positive debits, negative credits. The two-column
        // grid is a convenience for the person keying it, resolved here rather than in the domain.
        amount: (row.debit ?? 0) - (row.credit ?? 0),
        description: row.description || undefined,
        externalDocumentNo: row.externalDocumentNo || undefined,
      }));

    this.messages.clear();
    this.posting.set(true);

    try {
      const receipt = await this.finance.postJournal({
        batchCode: 'DEFAULT',
        documentNo: this.documentNo || undefined,
        description: this.description || undefined,
        lines,
      });

      // Everything the server said travels back, warnings included. A posting that went through on
      // an override should say so on the screen, not only in the audit log.
      this.messages.showAll(receipt.messages ?? []);

      this.reset();
    } catch (error) {
      // The server sends every reason at once, each with its own resolution. Showing them all is
      // the difference between one correction and four round trips.
      this.messages.showError(error, this.t('finance.journal.post'));
    } finally {
      this.posting.set(false);
    }
  }

  private reset(): void {
    this.rows.set([]);
    this.documentNo = '';
    this.description = '';
    this.addRow();
    this.addRow();
  }

  private rowOf(id: number): JournalRow | undefined {
    return this.rows().find((row) => row.id === id);
  }

  private parse(value: string): number | null {
    const trimmed = value.trim();

    if (trimmed === '') {
      return null;
    }

    const parsed = Number(trimmed);

    return Number.isFinite(parsed) && parsed !== 0 ? parsed : null;
  }
}

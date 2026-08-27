import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { PartyKind, PartyLedgerEntry } from '../../core/api/asap-api.models';
import { FinanceService } from '../../core/api/finance.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * One party's account, and where payments get matched to invoices.
 *
 * Applying is two clicks: pick what the money came from, then pick what it settles. The screen
 * refuses to offer the second half against an entry pulling the same way, so the commonest
 * mistake -- trying to settle one invoice with another -- is not reachable rather than refused
 * after the fact.
 */
@Component({
  selector: 'asap-party-ledger',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink],
  templateUrl: './party-ledger.html',
  styleUrl: './party-ledger.scss',
})
export class PartyLedger implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly finance = inject(FinanceService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);
  private readonly route = inject(ActivatedRoute);

  protected readonly entries = signal<PartyLedgerEntry[]>([]);
  protected readonly loading = signal(true);
  protected readonly applying = signal(false);
  protected readonly kind = signal<PartyKind>('Customer');
  protected readonly partyNo = signal('');

  /** The entry the money is coming from, once one has been picked. */
  protected readonly source = signal<PartyLedgerEntry | null>(null);

  protected openOnly = true;

  /** What is still outstanding across the whole account. */
  protected readonly outstanding = computed(() =>
    this.entries().reduce((total, entry) => total + (entry.isOpen ? entry.remainingAmount : 0), 0),
  );

  async ngOnInit(): Promise<void> {
    this.kind.set((this.route.snapshot.data['kind'] as PartyKind) ?? 'Customer');
    this.partyNo.set(this.route.snapshot.paramMap.get('partyNo') ?? '');

    await this.load();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canApply(): boolean {
    return this.auth.can('Finance.Party.Post');
  }

  protected listRoute(): string {
    return this.kind() === 'Customer' ? '/finance/customers' : '/finance/vendors';
  }

  /**
   * Whether this entry can be picked as the thing being settled, given what is already chosen.
   *
   * Nothing is selectable until a source is chosen, and then only entries pulling the other way.
   * Two invoices cannot settle one another, and offering the option would be offering a refusal.
   */
  protected canSettle(entry: PartyLedgerEntry): boolean {
    const from = this.source();

    return (
      this.canApply() &&
      from !== null &&
      entry.id !== from.id &&
      entry.isOpen &&
      Math.sign(entry.remainingAmount) !== Math.sign(from.remainingAmount)
    );
  }

  protected isSource(entry: PartyLedgerEntry): boolean {
    return this.source()?.id === entry.id;
  }

  protected select(entry: PartyLedgerEntry): void {
    if (!this.canApply() || !entry.isOpen) {
      return;
    }

    this.messages.clear();
    this.source.set(this.isSource(entry) ? null : entry);
  }

  protected clear(): void {
    this.source.set(null);
  }

  protected async apply(entry: PartyLedgerEntry): Promise<void> {
    const from = this.source();

    if (!from || this.applying()) {
      return;
    }

    this.messages.clear();
    this.applying.set(true);

    try {
      const receipt = await this.finance.applyEntries(this.kind(), from.id, entry.id);

      this.messages.showAll(receipt.messages ?? []);
      this.messages.showSuccess(this.appliedMessage(receipt.appliedAmount, receipt.closedEntries));

      this.source.set(null);
      await this.load();
    } catch (error) {
      this.messages.showError(error, this.t('finance.parties.apply'));
    } finally {
      this.applying.set(false);
    }
  }

  /**
   * What to say after applying.
   *
   * Three sentences rather than one with a count in it: "1 entry(s) settled" is the sort of thing
   * a user reads several times a day, and the brackets are a reminder that nobody finished the
   * sentence.
   */
  private appliedMessage(amount: number, closed: number): string {
    const values = { Amount: this.i18n.total(amount), Closed: closed };

    if (closed === 0) {
      return this.t('finance.parties.applied', values);
    }

    return this.t(
      closed === 1 ? 'finance.parties.appliedClosingOne' : 'finance.parties.appliedClosingMany',
      values,
    );
  }

  protected async load(): Promise<void> {
    this.loading.set(true);

    try {
      this.entries.set(
        await this.finance.partyEntries(this.kind(), this.partyNo(), this.openOnly),
      );
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

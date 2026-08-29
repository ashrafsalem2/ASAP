import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  BankAccountInfo,
  BankStatementDetail,
  BankStatementInfo,
  BankStatementLineInfo,
  OutstandingItemInfo,
} from '../../core/api/asap-api.models';
import { BankingService } from '../../core/api/banking.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * Agreeing a bank statement with the ledger.
 *
 * The screen is built around one number — the difference — because that number is the only thing
 * that says whether the reconciliation is true. Everything else on the page exists to explain it:
 * what the books say, what the bank says, and the items in between that the bank has not seen.
 *
 * A line is selected, then the entry it turned out to be. That order is deliberate: the statement
 * is the thing somebody is working through, and the ledger is what they are looking things up in.
 */
@Component({
  selector: 'asap-bank-reconciliation',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './bank-reconciliation.html',
  styleUrl: './finance.scss',
})
export class BankReconciliation implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(BankingService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly accounts = signal<BankAccountInfo[]>([]);
  protected readonly statements = signal<BankStatementInfo[]>([]);
  protected readonly detail = signal<BankStatementDetail | null>(null);
  protected readonly selectedLine = signal<BankStatementLineInfo | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);

  protected selectedAccount = '';

  /** Whether anything on this statement may still be changed. */
  protected readonly editable = computed(() => this.detail()?.statement.status === 'Open');

  async ngOnInit(): Promise<void> {
    this.loading.set(true);

    try {
      const accounts = await this.api.accounts();

      this.accounts.set(accounts);

      if (accounts.length > 0) {
        this.selectedAccount = accounts[0].code;
        await this.loadStatements();
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

  protected canMatch(): boolean {
    return this.auth.can('Finance.Bank.Update');
  }

  protected canReconcile(): boolean {
    return this.auth.can('Finance.Bank.Post');
  }

  protected async loadStatements(): Promise<void> {
    this.detail.set(null);
    this.selectedLine.set(null);

    try {
      const statements = await this.api.statements(this.selectedAccount);

      this.statements.set(statements);

      if (statements.length > 0) {
        await this.open(statements[0]);
      }
    } catch (error) {
      this.messages.showError(error);
    }
  }

  protected async open(statement: BankStatementInfo): Promise<void> {
    this.selectedLine.set(null);

    try {
      this.detail.set(await this.api.statement(statement.id));
    } catch (error) {
      this.messages.showError(error);
    }
  }

  protected select(line: BankStatementLineInfo): void {
    this.selectedLine.set(line.matchedEntryId ? null : line);
  }

  /** Accepts every suggestion at once, which is most of a month's work. */
  protected async acceptSuggestions(): Promise<void> {
    const detail = this.detail();

    if (!detail) {
      return;
    }

    this.busy.set(true);

    try {
      const suggestions = await this.api.suggestions(detail.statement.id);

      for (const suggestion of suggestions) {
        await this.api.match(suggestion.lineId, suggestion.entryId);
      }

      this.messages.showSuccess(
        this.t('finance.bank.suggestionsApplied', { count: suggestions.length }),
      );

      await this.refresh();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async matchTo(item: OutstandingItemInfo): Promise<void> {
    const line = this.selectedLine();

    if (!line) {
      return;
    }

    this.busy.set(true);

    try {
      await this.api.match(line.id, item.entryId);
      this.selectedLine.set(null);
      await this.refresh();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async unmatch(line: BankStatementLineInfo): Promise<void> {
    this.busy.set(true);

    try {
      await this.api.unmatch(line.id);
      await this.refresh();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async reconcile(): Promise<void> {
    const detail = this.detail();

    if (!detail) {
      return;
    }

    this.busy.set(true);

    try {
      await this.api.reconcile(detail.statement.id);
      this.messages.showSuccess(this.t('finance.bank.agreed', { no: detail.statement.no }));

      await this.loadStatements();
    } catch (error) {
      // The refusal says by how much and what is left over, which is the useful part.
      this.messages.showError(error);
      await this.refresh();
    } finally {
      this.busy.set(false);
    }
  }

  private async refresh(): Promise<void> {
    const detail = this.detail();

    if (!detail) {
      return;
    }

    try {
      this.detail.set(await this.api.statement(detail.statement.id));

      this.statements.set(
        this.statements().map((s) =>
          s.id === detail.statement.id ? this.detail()!.statement : s,
        ),
      );
    } catch (error) {
      this.messages.showError(error);
    }
  }
}

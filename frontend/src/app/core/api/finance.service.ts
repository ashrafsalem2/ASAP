import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  AgedAnalysis,
  TaxCodeSummary,
  TaxReturn,
  ApplicationReceipt,
  BalanceSheet,
  BranchPerformance,
  GlAccount,
  GlEntry,
  IncomeStatement,
  MenuNode,
  Party,
  PartyKind,
  PartyLedgerEntry,
  PostJournalLine,
  PostingReceipt,
  TrialBalance,
} from './asap-api.models';

/**
 * Talks to the Finance endpoints.
 *
 * Everything returns a promise rather than an observable. These are one-shot request-and-answer
 * calls, and a promise is what the components actually want; the streams that genuinely benefit
 * from observables are elsewhere, and can use them there.
 */
@Injectable({ providedIn: 'root' })
export class FinanceService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api`;

  /** The menu, already filtered to what the caller may open. */
  navigation(): Promise<MenuNode[]> {
    return firstValueFrom(this.http.get<MenuNode[]>(`${this.base}/navigation`));
  }

  /** The chart of accounts for the active company. */
  accounts(): Promise<GlAccount[]> {
    return firstValueFrom(this.http.get<GlAccount[]>(`${this.base}/finance/accounts`));
  }

  /** Posted ledger entries, most recent transaction first. */
  entries(filter: { accountNo?: string; transactionNo?: number; take?: number } = {}): Promise<GlEntry[]> {
    let params = new HttpParams();

    if (filter.accountNo) {
      params = params.set('accountNo', filter.accountNo);
    }

    if (filter.transactionNo !== undefined) {
      params = params.set('transactionNo', filter.transactionNo);
    }

    if (filter.take !== undefined) {
      params = params.set('take', filter.take);
    }

    return firstValueFrom(this.http.get<GlEntry[]>(`${this.base}/finance/entries`, { params }));
  }

  /** Posts journal lines. Rejects with a problem carrying every reason it was refused. */
  postJournal(request: {
    batchCode: string;
    lines: PostJournalLine[];
    documentNo?: string;
    description?: string;
    overrideReason?: string;
  }): Promise<PostingReceipt> {
    return firstValueFrom(
      this.http.post<PostingReceipt>(`${this.base}/finance/journals/post`, request),
    );
  }

  /** Reverses a posted transaction. */
  reverse(transactionNo: number, reason: string): Promise<PostingReceipt> {
    return firstValueFrom(
      this.http.post<PostingReceipt>(`${this.base}/finance/journals/reverse`, {
        transactionNo,
        reason,
      }),
    );
  }

  /** The trial balance for a date range. */
  trialBalance(from: string, to: string, includeAll: boolean): Promise<TrialBalance> {
    const params = new HttpParams().set('from', from).set('to', to).set('includeAll', includeAll);

    return firstValueFrom(
      this.http.get<TrialBalance>(`${this.base}/finance/reports/trial-balance`, { params }),
    );
  }

  /** What the company earned over a range, optionally beside the same range a year earlier. */
  incomeStatement(
    from: string,
    to: string,
    comparePreviousYear: boolean,
    includeAll: boolean,
  ): Promise<IncomeStatement> {
    const params = new HttpParams()
      .set('from', from)
      .set('to', to)
      .set('comparePreviousYear', comparePreviousYear)
      .set('includeAll', includeAll);

    return firstValueFrom(
      this.http.get<IncomeStatement>(`${this.base}/finance/reports/income-statement`, { params }),
    );
  }

  /** What each branch earned and spent over a range, and what was charged to none of them. */
  branchPerformance(from: string, to: string, includeInactive = false): Promise<BranchPerformance> {
    const params = new HttpParams()
      .set('from', from)
      .set('to', to)
      .set('includeInactive', includeInactive);

    return firstValueFrom(
      this.http.get<BranchPerformance>(`${this.base}/finance/reports/branch-performance`, {
        params,
      }),
    );
  }

  /** What the company owned and owed on a given day. */
  balanceSheet(asAt: string, includeAll: boolean): Promise<BalanceSheet> {
    const params = new HttpParams().set('asAt', asAt).set('includeAll', includeAll);

    return firstValueFrom(
      this.http.get<BalanceSheet>(`${this.base}/finance/reports/balance-sheet`, { params }),
    );
  }

  /** Customers or vendors, with what each owes. */
  parties(kind: PartyKind): Promise<Party[]> {
    return firstValueFrom(this.http.get<Party[]>(`${this.base}/finance/${this.path(kind)}`));
  }

  /** One party's ledger entries, most recent first. */
  partyEntries(kind: PartyKind, partyNo: string, openOnly = false): Promise<PartyLedgerEntry[]> {
    const params = new HttpParams().set('openOnly', openOnly);

    return firstValueFrom(
      this.http.get<PartyLedgerEntry[]>(
        `${this.base}/finance/${this.path(kind)}/${encodeURIComponent(partyNo)}/entries`,
        { params },
      ),
    );
  }

  /** Records which payment settled which invoice. */
  applyEntries(
    kind: PartyKind,
    fromEntryId: string,
    toEntryId: string,
    amount?: number,
  ): Promise<ApplicationReceipt> {
    return firstValueFrom(
      this.http.post<ApplicationReceipt>(`${this.base}/finance/${this.path(kind)}/apply`, {
        fromEntryId,
        toEntryId,
        amount,
      }),
    );
  }

  /** What is outstanding, split by how late it is. */
  agedAnalysis(kind: PartyKind, asAt: string, bands?: string): Promise<AgedAnalysis> {
    let params = new HttpParams().set('kind', kind).set('asAt', asAt);

    if (bands) {
      params = params.set('bands', bands);
    }

    return firstValueFrom(
      this.http.get<AgedAnalysis>(`${this.base}/finance/reports/aged-analysis`, { params }),
    );
  }

  /** The tax codes a document line can carry. */
  taxCodes(): Promise<TaxCodeSummary[]> {
    return firstValueFrom(this.http.get<TaxCodeSummary[]>(`${this.base}/finance/tax-codes`));
  }

  /** What is owed to the tax authority for a period. */
  taxReturn(from: string, to: string, includeFiled: boolean): Promise<TaxReturn> {
    const params = new HttpParams()
      .set('from', from)
      .set('to', to)
      .set('includeFiled', includeFiled);

    return firstValueFrom(
      this.http.get<TaxReturn>(`${this.base}/finance/reports/tax-return`, { params }),
    );
  }

  /** The two ledgers differ only in their route segment. */
  private path(kind: PartyKind): string {
    return kind === 'Customer' ? 'customers' : 'vendors';
  }
}

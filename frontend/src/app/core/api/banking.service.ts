import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  BankAccountInfo,
  BankStatementDetail,
  BankStatementInfo,
} from './asap-api.models';

/** Talks to the bank account and reconciliation endpoints. */
@Injectable({ providedIn: 'root' })
export class BankingService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/finance/banking`;

  /** The bank accounts the company holds. */
  accounts(): Promise<BankAccountInfo[]> {
    return firstValueFrom(this.http.get<BankAccountInfo[]>(`${this.base}/accounts`));
  }

  /** One account's statements, most recent first. */
  statements(code: string): Promise<BankStatementInfo[]> {
    return firstValueFrom(
      this.http.get<BankStatementInfo[]>(
        `${this.base}/accounts/${encodeURIComponent(code)}/statements`,
      ),
    );
  }

  /** One statement, its lines and where the reconciliation stands. */
  statement(statementId: string): Promise<BankStatementDetail> {
    return firstValueFrom(
      this.http.get<BankStatementDetail>(`${this.base}/statements/${statementId}`),
    );
  }

  /** Which entry each unmatched line looks like, where that is not a guess. */
  suggestions(statementId: string): Promise<{ lineId: string; entryId: string }[]> {
    return firstValueFrom(
      this.http.get<{ lineId: string; entryId: string }[]>(
        `${this.base}/statements/${statementId}/suggestions`,
      ),
    );
  }

  /** Records that a line is a particular ledger entry. */
  match(lineId: string, entryId: string): Promise<unknown> {
    return firstValueFrom(
      this.http.post(`${this.base}/statements/lines/${lineId}/match`, { entryId }),
    );
  }

  /** Takes a match back off a line. */
  unmatch(lineId: string): Promise<unknown> {
    return firstValueFrom(this.http.delete(`${this.base}/statements/lines/${lineId}/match`));
  }

  /** Agrees a statement, if and only if it proves. */
  reconcile(statementId: string): Promise<unknown> {
    return firstValueFrom(
      this.http.post(`${this.base}/statements/${statementId}/reconcile`, {}),
    );
  }
}

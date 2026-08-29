import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  Branch,
  CalculatePayrollRequest,
  Companies,
  Employee,
  EmployeeSaved,
  Entitlements,
  HireRequest,
  LeaveEntitlement,
  LeaveRequest,
  LeaveRequestInput,
  LeaveSaved,
  LeavingRequest,
  PayrollRun,
  PayrollRunSummary,
  PayrollSaved,
  TransferRequest,
  BranchCostRow,
  HeadcountRow,
  Turnover,
} from './asap-api.models';

/** Talks to the human resources endpoints, and to the branch list every screen here needs. */
@Injectable({ providedIn: 'root' })
export class HrService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiBaseUrl}/api/hr`;
  private readonly root = `${environment.apiBaseUrl}/api`;

  /** The branches of the company being worked in. */
  branches(includeInactive = false): Promise<Branch[]> {
    const params = includeInactive
      ? new HttpParams().set('includeInactive', 'true')
      : new HttpParams();

    return firstValueFrom(this.http.get<Branch[]>(`${this.root}/branches`, { params }));
  }

  /** The companies in this tenant, and which one is current. */
  companies(): Promise<Companies> {
    return firstValueFrom(this.http.get<Companies>(`${this.root}/companies`));
  }

  /** Employees, most recently hired first. */
  employees(includeLeavers = false): Promise<Employee[]> {
    const params = includeLeavers
      ? new HttpParams().set('includeLeavers', 'true')
      : new HttpParams();

    return firstValueFrom(this.http.get<Employee[]>(`${this.base}/employees`, { params }));
  }

  /** One employee and everywhere they have worked. */
  employee(employeeNo: string): Promise<Employee> {
    return firstValueFrom(
      this.http.get<Employee>(`${this.base}/employees/${encodeURIComponent(employeeNo)}`),
    );
  }

  /** Hires somebody from a date. */
  hire(request: HireRequest): Promise<EmployeeSaved> {
    return firstValueFrom(this.http.post<EmployeeSaved>(`${this.base}/employees`, request));
  }

  /** Moves somebody to another branch, closing the assignment they are on. */
  transfer(employeeNo: string, request: TransferRequest): Promise<EmployeeSaved> {
    return firstValueFrom(
      this.http.post<EmployeeSaved>(
        `${this.base}/employees/${encodeURIComponent(employeeNo)}/transfer`,
        request,
      ),
    );
  }

  /** Records that somebody has left, and works out what they are owed. */
  recordLeaving(employeeNo: string, request: LeavingRequest): Promise<EmployeeSaved> {
    return firstValueFrom(
      this.http.post<EmployeeSaved>(
        `${this.base}/employees/${encodeURIComponent(employeeNo)}/leaving`,
        request,
      ),
    );
  }

  /** What the company owes its staff in unused leave and end of service. */
  entitlements(on?: string): Promise<Entitlements> {
    const params = on ? new HttpParams().set('on', on) : new HttpParams();

    return firstValueFrom(this.http.get<Entitlements>(`${this.base}/entitlements`, { params }));
  }

  /** Leave requests, most recent first. */
  leaveRequests(employeeNo?: string, from?: string, to?: string): Promise<LeaveRequest[]> {
    let params = new HttpParams();

    if (employeeNo) {
      params = params.set('employeeNo', employeeNo);
    }

    if (from) {
      params = params.set('from', from);
    }

    if (to) {
      params = params.set('to', to);
    }

    return firstValueFrom(this.http.get<LeaveRequest[]>(`${this.base}/leave`, { params }));
  }

  /** What one employee has earned, taken and has left. */
  leaveBalance(employeeNo: string, on?: string): Promise<LeaveEntitlement> {
    const params = on ? new HttpParams().set('on', on) : new HttpParams();

    return firstValueFrom(
      this.http.get<LeaveEntitlement>(
        `${this.base}/leave/balance/${encodeURIComponent(employeeNo)}`,
        { params },
      ),
    );
  }

  /** Asks for leave. */
  requestLeave(request: LeaveRequestInput): Promise<LeaveSaved> {
    return firstValueFrom(this.http.post<LeaveSaved>(`${this.base}/leave`, request));
  }

  /** Grants, refuses or withdraws a request. */
  decideLeave(
    requestNo: string,
    decision: 'approve' | 'reject' | 'cancel',
    note?: string,
  ): Promise<LeaveRequest> {
    return firstValueFrom(
      this.http.post<LeaveRequest>(
        `${this.base}/leave/${encodeURIComponent(requestNo)}/${decision}`,
        { note: note ?? null },
      ),
    );
  }

  /** Payroll runs, most recent first. */
  payrollRuns(): Promise<PayrollRunSummary[]> {
    return firstValueFrom(this.http.get<PayrollRunSummary[]>(`${this.base}/payroll`));
  }

  /** One run, its lines, and how each divides between branches. */
  payrollRun(runNo: string): Promise<PayrollRun> {
    return firstValueFrom(
      this.http.get<PayrollRun>(`${this.base}/payroll/${encodeURIComponent(runNo)}`),
    );
  }

  /** Works out what everybody is owed for a period, without committing to it. */
  calculate(request: CalculatePayrollRequest): Promise<PayrollSaved> {
    return firstValueFrom(this.http.post<PayrollSaved>(`${this.base}/payroll`, request));
  }

  /** Commits a run to the ledger, charging each branch what it actually cost. */
  post(runNo: string, overrideReason?: string): Promise<PayrollSaved> {
    return firstValueFrom(
      this.http.post<PayrollSaved>(`${this.base}/payroll/${encodeURIComponent(runNo)}/post`, {
        overrideReason: overrideReason ?? null,
      }),
    );
  }

  /** Throws away a draft run. A posted run is reversed instead. */
  discard(runNo: string): Promise<void> {
    return firstValueFrom(
      this.http.delete<void>(`${this.base}/payroll/${encodeURIComponent(runNo)}`),
    );
  }

  /** How many people are at each branch, on a day. */
  headcount(on?: string): Promise<HeadcountRow[]> {
    const query = on ? `?on=${on}` : '';

    return firstValueFrom(this.http.get<HeadcountRow[]>(`${this.base}/reports/headcount${query}`));
  }

  /** What each branch's staff cost, on a day, at contractual rates. */
  costByBranch(on?: string): Promise<BranchCostRow[]> {
    const query = on ? `?on=${on}` : '';

    return firstValueFrom(
      this.http.get<BranchCostRow[]>(`${this.base}/reports/cost-by-branch${query}`),
    );
  }

  /** How many people came and went over a period, and the rate it comes to. */
  turnover(fromDate: string, toDate: string): Promise<Turnover> {
    const query = new URLSearchParams({ fromDate, toDate });

    return firstValueFrom(this.http.get<Turnover>(`${this.base}/reports/turnover?${query}`));
  }
}

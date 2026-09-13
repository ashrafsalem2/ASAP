import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { BankAccountInfo, Party, PaymentRunRow } from '../../core/api/asap-api.models';
import { AuthService } from '../../core/auth/auth.service';
import { BankingService } from '../../core/api/banking.service';
import { FinanceService } from '../../core/api/finance.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * Vendor payment runs: propose, release the bank file, post what was paid.
 *
 * Each stage has its own button and only the button for the stage the run is at is offered. The
 * line between exported and posted is the one people most want to blur, and the screen will not
 * blur it for them.
 *
 * The file's fingerprint is shown in full, not truncated, because the point of it is to be compared
 * character for character against what the bank's upload screen shows.
 */
@Component({
  selector: 'asap-payment-runs',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './payment-runs.html',
  styleUrl: './finance.scss',
})
export class PaymentRuns implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(FinanceService);
  private readonly banking = inject(BankingService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly runs = signal<PaymentRunRow[]>([]);
  protected readonly accounts = signal<BankAccountInfo[]>([]);
  protected readonly vendors = signal<Party[]>([]);
  protected readonly selected = signal<PaymentRunRow | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);

  protected bankAccountCode = '';
  protected paymentDate = '';
  protected dueByDate = '';

  protected vendorNo = '';
  protected vendorIban = '';
  protected vendorBic = '';
  protected vendorBankName = '';

  async ngOnInit(): Promise<void> {
    const today = new Date().toISOString().slice(0, 10);

    this.paymentDate = today;
    this.dueByDate = today;

    await this.reload();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canPropose(): boolean {
    return this.auth.can('Finance.Payment.Create');
  }

  protected canRelease(): boolean {
    return this.auth.can('Finance.Payment.Post');
  }

  protected canEditVendors(): boolean {
    return this.auth.can('Finance.Party.Update');
  }

  protected statusLabel(status: PaymentRunRow['status']): string {
    return this.t(`finance.paymentRuns.status.${status}` as TranslationKey);
  }

  protected hasBadIban(run: PaymentRunRow): boolean {
    return run.lines.some((line) => !line.ibanValid);
  }

  protected select(run: PaymentRunRow): void {
    this.selected.set(run);
  }

  protected chooseVendor(): void {
    const vendor = this.vendors().find((v) => v.no === this.vendorNo);

    this.vendorIban = vendor?.iban ?? '';
    this.vendorBic = vendor?.bic ?? '';
    this.vendorBankName = vendor?.bankName ?? '';
  }

  protected async propose(): Promise<void> {
    if (!this.bankAccountCode || !this.paymentDate || !this.dueByDate) {
      return;
    }

    await this.act(async () => {
      const result = await this.api.proposePaymentRun({
        bankAccountCode: this.bankAccountCode,
        paymentDate: this.paymentDate,
        dueByDate: this.dueByDate,
      });

      this.messages.showAll((result.messages ?? []).filter((m) => m.severity !== 'Success'));
      this.messages.showSuccess(
        this.t('finance.paymentRuns.proposed', { No: result.run.no, Count: result.run.lines.length }),
      );

      return result.run.no;
    });
  }

  protected async removeLine(run: PaymentRunRow, lineNo: number): Promise<void> {
    await this.act(async () => (await this.api.removePaymentRunLine(run.no, lineNo)).run.no);
  }

  protected async export(run: PaymentRunRow): Promise<void> {
    await this.act(async () => {
      const file = await this.api.exportPaymentRun(run.no);

      this.messages.showSuccess(
        this.t('finance.paymentRuns.exported', { Count: file.transferCount, Total: this.i18n.total(file.controlSum) }),
      );

      await this.download(run.no);

      return run.no;
    });
  }

  /** Fetches the stored file with the signed-in session and hands it to the browser to save. */
  protected async download(runNo: string): Promise<void> {
    try {
      const blob = await this.api.paymentRunFile(runNo);
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');

      link.href = url;
      link.download = `${runNo}.xml`;
      link.click();

      URL.revokeObjectURL(url);
    } catch (error) {
      this.messages.showError(error);
    }
  }

  protected async post(run: PaymentRunRow): Promise<void> {
    await this.act(async () => {
      const result = await this.api.postPaymentRun(run.no);

      this.messages.showAll((result.messages ?? []).filter((m) => m.severity !== 'Success'));
      this.messages.showSuccess(
        this.t('finance.paymentRuns.posted', { No: run.no, Transaction: result.run.transactionNo ?? 0 }),
      );

      return run.no;
    });
  }

  protected async cancel(run: PaymentRunRow): Promise<void> {
    await this.act(async () => (await this.api.cancelPaymentRun(run.no)).run.no);
  }

  protected async saveBankDetails(): Promise<void> {
    if (!this.vendorNo) {
      return;
    }

    this.busy.set(true);

    try {
      await this.api.setVendorBankDetails(this.vendorNo, {
        iban: this.vendorIban || null,
        bic: this.vendorBic || null,
        bankName: this.vendorBankName || null,
      });

      this.messages.showSuccess(this.t('finance.paymentRuns.bankSaved', { VendorNo: this.vendorNo }));
      this.vendors.set(await this.api.parties('Vendor'));
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  private async act(work: () => Promise<string>): Promise<void> {
    this.busy.set(true);

    try {
      const no = await work();

      await this.reload(no);
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  private async reload(keepNo?: string): Promise<void> {
    this.loading.set(true);

    try {
      const [runs, accounts, vendors] = await Promise.all([
        this.api.paymentRuns(),
        this.accounts().length > 0 ? Promise.resolve(this.accounts()) : this.banking.accounts(),
        this.vendors().length > 0 ? Promise.resolve(this.vendors()) : this.api.parties('Vendor'),
      ]);

      this.runs.set(runs);
      this.accounts.set(accounts.filter((account) => account.isActive));
      this.vendors.set(vendors);

      if (!this.bankAccountCode && this.accounts().length === 1) {
        this.bankAccountCode = this.accounts()[0].code;
      }

      this.selected.set(runs.find((run) => run.no === (keepNo ?? this.selected()?.no)) ?? null);
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}

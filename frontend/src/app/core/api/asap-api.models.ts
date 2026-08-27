import { AsapMessage } from './asap-message';

/** What the server returns when a sign-in succeeds. */
export interface SignInResponse {
  accessToken: string;
  expiresAt: string;
  refreshToken: string;
  refreshExpiresAt: string;
  user: {
    id: string;
    userName: string;
    displayName: string;
    culture?: string;
    isSuperUser: boolean;
    defaultCompanyId?: string;
    defaultBranchId?: string;
  };
}

/** Who the caller is and what they may do in the company they are working in. */
export interface CurrentUser {
  userId: string;
  userName: string;
  displayName: string;
  culture?: string;
  isSuperUser: boolean;
  tenantId?: string;
  companyId?: string;
  branchId?: string;
  branchCode?: string;
  branchName?: string;
  permissions: string[];
}

/** A company the signed-in user may work in. */
export interface Company {
  id: string;
  code: string;
  name: string;
  nameArabic?: string;
  baseCurrencyCode: string;
}

/** One entry in the menu, already filtered to what the caller may open. */
export interface MenuNode {
  id: string;
  module: string;
  displayName: string;
  kind: 'Group' | 'Page' | 'Report' | 'Setup' | 'Task';
  route?: string;
  icon?: string;
  children: MenuNode[];
}

/** One line of the chart of accounts. */
export interface GlAccount {
  id: string;
  no: string;
  name: string;
  nameArabic?: string;
  accountType: 'Posting' | 'Heading' | 'Total' | 'BeginTotal' | 'EndTotal';
  category: string;
  indentation: number;
  allowsDirectPosting: boolean;
  isBlocked: boolean;
  balance: number;
}

/** A posted ledger entry. */
export interface GlEntry {
  id: string;
  postingDate: string;
  transactionNo: number;
  accountNo: string;
  description: string;
  debitAmount: number;
  creditAmount: number;
  documentNo?: string;
  sourceCode: string;
}

/** One line being posted. */
export interface PostJournalLine {
  /** An account number, or a customer or vendor number when the type says so. */
  accountNo: string;
  amount: number;
  description?: string;
  balancingAccountNo?: string;
  postingDate?: string;

  /** What the line posts to. Defaults to a general ledger account. */
  accountType?: 'GlAccount' | 'Customer' | 'Vendor';

  /** The other side's reference, such as the number on a vendor's own invoice. */
  externalDocumentNo?: string;

  /** The tax to apply. ASAP works out the tax and posts it beside the line. */
  taxCode?: string;

  /** Whether the amount already contains the tax, as a shelf price does. */
  taxIncludedInAmount?: boolean;
}

/** What a successful posting produced. */
export interface PostingReceipt {
  transactionNo: number;
  documentNo?: string;
  entryCount: number;
  totalAmount: number;
  messages?: AsapMessage[];
}

/** One account on the trial balance. */
export interface TrialBalanceRow {
  accountNo: string;
  name: string;
  nameArabic?: string;
  accountType: string;
  category: string;
  indentation: number;
  openingBalance: number;
  periodDebit: number;
  periodCredit: number;
  closingBalance: number;
}

/** The trial balance for a date range. */
export interface TrialBalance {
  from: string;
  to: string;
  currencyCode: string;
  rows: TrialBalanceRow[];
  totalDebit: number;
  totalCredit: number;
  isBalanced: boolean;
}

/** An item as the client sees it. */
export interface Item {
  no: string;
  description: string;
  descriptionArabic?: string;
  costingMethod: 'Fifo' | 'Average' | 'Standard' | 'Specific';
  unitCost: number;
  unitPrice: number;
  quantityOnHand: number;
  reorderPoint: number;
  allowNegativeInventory?: boolean | null;
}

/** A place stock is held. */
export interface StockLocation {
  code: string;
  name: string;
  nameArabic?: string;
  isSellable: boolean;
  isInTransit: boolean;
  isBlocked: boolean;
}

/** What is on hand for one item at one location. */
export interface StockOnHandRow {
  itemNo: string;
  description: string;
  descriptionArabic?: string;
  locationCode: string;
  quantity: number;
  isNegative: boolean;
}

/** One recorded stock movement. */
export interface StockMovement {
  postingDate: string;
  transactionNo: number;
  itemNo: string;
  locationCode: string;
  entryType: string;
  quantity: number;
  remainingQuantity: number;
  documentNo?: string;
  sourceCode: string;
  wentNegative: boolean;
}

/** One movement being posted. */
export interface StockMovementRequest {
  itemNo: string;
  locationCode: string;
  quantity: number;
  unitCost?: number;
  entryType: string;
  salesAmount?: number;
}

/** What a stock posting produced. */
export interface StockPostingReceipt {
  transactionNo: number;
  entryCount: number;
  costAmount: number;
  estimatedCostAmount: number;
  messages?: AsapMessage[];
}

/** What a settlement run corrected. */
export interface SettlementReceipt {
  itemsExamined: number;
  applicationsSettled: number;
  totalCorrection: number;
  messages?: AsapMessage[];
}

/** One account on the income statement. */
export interface IncomeStatementRow {
  accountNo: string;
  name: string;
  nameArabic?: string;
  indentation: number;
  amount: number;
  comparative?: number;
}

/** One block of the income statement, with its own subtotal. */
export interface IncomeStatementSection {
  category: 'Income' | 'CostOfGoodsSold' | 'Expense';
  rows: IncomeStatementRow[];
  total: number;
  comparativeTotal?: number;
}

/** What the company earned over a range. */
export interface IncomeStatement {
  from: string;
  to: string;
  comparativeFrom?: string;
  comparativeTo?: string;
  currencyCode: string;
  sections: IncomeStatementSection[];
  grossProfit: number;
  comparativeGrossProfit?: number;
  netProfit: number;
  comparativeNetProfit?: number;
}

/** One line on the balance sheet. */
export interface BalanceSheetRow {
  /** Absent on a line ASAP worked out rather than read from an account. */
  accountNo?: string;
  name: string;
  nameArabic?: string;
  indentation: number;
  amount: number;
  isComputed: boolean;
}

/** One block of the balance sheet. */
export interface BalanceSheetSection {
  category: 'Assets' | 'Liabilities' | 'Equity';
  rows: BalanceSheetRow[];
  total: number;
}

/** What the company owned and owed on a given day. */
export interface BalanceSheet {
  asAt: string;
  currencyCode: string;
  sections: BalanceSheetSection[];
  totalAssets: number;
  totalLiabilitiesAndEquity: number;
  isBalanced: boolean;

  /** Profit since the year began, sitting in equity until the year-end transfer runs. */
  resultForTheYear: number;

  /** Profit from earlier years whose year-end transfer never ran. Normally zero. */
  untransferredPriorResult: number;
}

/** A tax a document line can carry. */
export interface TaxCodeSummary {
  code: string;
  description: string;
  descriptionArabic?: string;
  kind: 'Standard' | 'ZeroRated' | 'Exempt' | 'ReverseCharge';

  /** The rate in force today, for showing beside the code. A posting resolves its own. */
  percentage: number;
}

/** One line of a tax return: everything at one rate, on one side. */
export interface TaxReturnLine {
  taxCodeNo: string;
  description: string;
  descriptionArabic?: string;
  kind: string;
  direction: 'Output' | 'Input';
  percentage: number;
  baseAmount: number;
  taxAmount: number;
  entryCount: number;
}

/** What the company owes the tax authority for a period, or is owed by it. */
export interface TaxReturn {
  from: string;
  to: string;
  currencyCode: string;
  lines: TaxReturnLine[];
  outputBase: number;
  outputTax: number;
  inputBase: number;
  inputTax: number;

  /** Positive is owed to the authority; negative is a refund due. */
  netPayable: number;
  exemptBase: number;
  zeroRatedBase: number;

  /** Above zero means these figures are not what was declared. */
  entriesAlreadyFiled: number;
}

/** Which subsidiary ledger something belongs to. */
export type PartyKind = 'Customer' | 'Vendor';

/** A customer or vendor. */
export interface Party {
  no: string;
  name: string;
  nameArabic?: string;
  paymentTermsDays: number;
  creditLimit: number;
  balance: number;
  isOverLimit: boolean;
  controlAccountNo?: string;
  isBlocked: boolean;
  email?: string;
  phone?: string;
}

/** One entry on a party's account. */
export interface PartyLedgerEntry {
  id: string;
  postingDate: string;
  dueDate: string;
  transactionNo: number;
  documentType: string;
  documentNo?: string;
  externalDocumentNo?: string;
  description: string;
  amount: number;

  /** What is still unsettled, on the same sign as the amount. */
  remainingAmount: number;
  isOpen: boolean;
  daysOverdue: number;
}

/** What an application changed. */
export interface ApplicationReceipt {
  appliedAmount: number;
  fromRemaining: number;
  toRemaining: number;
  closedEntries: number;
  messages?: AsapMessage[];
}

/** What one party owes, split by how late it is. */
export interface AgedAnalysisRow {
  partyNo: string;
  name: string;
  nameArabic?: string;
  buckets: number[];
  total: number;
  oldestDocumentNo?: string;
  oldestDaysOverdue: number;
  creditLimit: number;
  isOverLimit: boolean;
}

/** What is owed, and how late it is. */
export interface AgedAnalysis {
  asAt: string;
  kind: PartyKind;
  currencyCode: string;

  /** Band codes such as `NotDue` and `Over90`, translated by the client. */
  bandLabels: string[];
  rows: AgedAnalysisRow[];
  bucketTotals: number[];
  total: number;
}

/** Where a transfer stands. */
export type TransferStatus =
  | 'Open'
  | 'Released'
  | 'Shipped'
  | 'PartiallyReceived'
  | 'Received'
  | 'Cancelled';

/** One line of a transfer. */
export interface TransferLine {
  lineNo: number;
  itemNo: string;
  description: string;
  descriptionArabic?: string;
  quantity: number;
  quantityShipped: number;
  quantityReceived: number;

  /** Sent but not yet arrived: what the in-transit location is holding for this line. */
  inTransit: number;
}

/** A movement of stock from one location to another. */
export interface Transfer {
  no: string;
  fromLocationCode: string;
  toLocationCode: string;
  status: TransferStatus;
  shipmentDate: string;
  expectedReceiptDate?: string;
  shippedOn?: string;
  receivedOn?: string;
  description?: string;
  lines: TransferLine[];
}

/** What a client sends to raise a transfer. */
export interface CreateTransferRequest {
  fromLocationCode: string;
  toLocationCode: string;
  lines: { itemNo: string; quantity: number }[];
  description?: string;
  expectedReceiptDate?: string;
}

/** What raising a transfer produced. */
export interface TransferCreated {
  transfer: Transfer;
  messages?: AsapMessage[];
}

/** What shipping or receiving produced. */
export interface TransferMoveReceipt {
  transferNo: string;
  transactionNo: number;
  lineCount: number;
  status: TransferStatus;
  messages?: AsapMessage[];
}

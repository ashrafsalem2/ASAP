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
  accountNo: string;
  amount: number;
  description?: string;
  balancingAccountNo?: string;
  postingDate?: string;
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

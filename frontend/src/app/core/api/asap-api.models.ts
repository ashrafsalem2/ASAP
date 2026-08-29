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
  nameArabic?: string | null;
  baseCurrencyCode: string;
  registrationNo?: string | null;
  taxRegistrationNo?: string | null;
  fiscalYearStartMonth?: number;

  /**
   * Whether anything has been posted. The currency and the year's opening month describe how the
   * existing figures were measured, so once this is true they are settled.
   */
  hasPostedEntries?: boolean;

  /** Set by the company list; the session payload leaves it out. */
  isActive?: boolean;
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

  /** Withdrawn from use. Still valued and still reportable; simply not sellable. */
  isBlocked: boolean;
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

/** Where a purchase order stands. */
export type PurchaseOrderStatus =
  | 'Open'
  | 'Released'
  | 'PartiallyReceived'
  | 'Received'
  | 'Invoiced'
  | 'Cancelled';

/** One line of a purchase order. */
export interface PurchaseOrderLine {
  lineNo: number;
  type: 'Item' | 'GlAccount';
  no?: string;
  description: string;
  locationCode?: string;
  quantity: number;
  directUnitCost: number;
  taxCode?: string;
  lineAmount: number;
  quantityReceived: number;
  quantityInvoiced: number;

  /** How much is still to arrive. */
  outstandingToReceive: number;

  /** Arrived and still awaiting an invoice. What the accrual is built from. */
  receivedNotInvoiced: number;
}

/** An order placed with a vendor. */
export interface PurchaseOrder {
  no: string;
  vendorNo: string;
  vendorName: string;
  status: PurchaseOrderStatus;
  orderDate: string;
  expectedReceiptDate?: string;
  locationCode?: string;
  vendorOrderNo?: string;
  description?: string;
  totalAmount: number;
  isEditable: boolean;
  lines: PurchaseOrderLine[];
}

/** What a client sends to raise a purchase order. */
export interface CreatePurchaseOrderRequest {
  vendorNo: string;
  lines: {
    type: 'Item' | 'GlAccount';
    no: string;
    quantity: number;
    directUnitCost: number;
    description?: string;
    taxCode?: string;
    locationCode?: string;
  }[];
  locationCode?: string;
  expectedReceiptDate?: string;
  description?: string;
  vendorOrderNo?: string;
}

/** What raising an order produced. */
export interface PurchaseOrderCreated {
  order: PurchaseOrder;
  messages?: AsapMessage[];
}

/** What a goods receipt moved. */
export interface GoodsReceiptResult {
  orderNo: string;
  transactionNo: number;
  lineCount: number;
  value: number;
  status: PurchaseOrderStatus;
  messages?: AsapMessage[];
}

/** What a vendor invoice posted. */
export interface PurchaseInvoiceResult {
  orderNo: string;
  transactionNo: number;
  documentNo: string;
  netAmount: number;
  taxAmount: number;
  totalAmount: number;
  status: PurchaseOrderStatus;
  messages?: AsapMessage[];
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

/** Where a sales order stands. */
export type SalesOrderStatus =
  | 'Open'
  | 'Released'
  | 'PartiallyShipped'
  | 'Shipped'
  | 'Invoiced'
  | 'Cancelled';

/** One line of a sales order. */
export interface SalesOrderLine {
  lineNo: number;
  type: 'Item' | 'GlAccount';
  no?: string;
  description: string;
  locationCode?: string;
  quantity: number;
  unitPrice: number;

  /** Held as a percentage rather than folded into the price, so it stays reportable. */
  discountPercent: number;
  taxCode?: string;
  lineAmount: number;
  quantityShipped: number;
  quantityInvoiced: number;

  /** How much is still to go out. */
  outstandingToShip: number;

  /** Gone and still unbilled. Revenue the company has earned and not yet asked for. */
  shippedNotInvoiced: number;
}

/** An order taken from a customer. */
export interface SalesOrder {
  no: string;
  customerNo: string;
  customerName: string;
  status: SalesOrderStatus;
  orderDate: string;
  requestedDeliveryDate?: string;
  locationCode?: string;
  customerOrderNo?: string;
  description?: string;
  totalAmount: number;
  isEditable: boolean;
  lines: SalesOrderLine[];
}

/** What a client sends to take a sales order. */
export interface CreateSalesOrderRequest {
  customerNo: string;
  lines: {
    type: 'Item' | 'GlAccount';
    no: string;
    quantity: number;
    unitPrice?: number;
    discountPercent?: number;
    description?: string;
    taxCode?: string;
    locationCode?: string;
  }[];
  locationCode?: string;
  requestedDeliveryDate?: string;
  description?: string;
  customerOrderNo?: string;
}

/** What taking an order produced. */
export interface SalesOrderCreated {
  order: SalesOrder;
  messages?: AsapMessage[];
}

/** What a shipment moved. The cost has nothing to do with what the customer pays. */
export interface SalesShipmentResult {
  orderNo: string;
  transactionNo: number;
  lineCount: number;
  costAmount: number;
  status: SalesOrderStatus;
  messages?: AsapMessage[];
}

/** What a sales invoice posted. */
export interface SalesInvoiceResult {
  orderNo: string;
  transactionNo: number;
  documentNo: string;
  netAmount: number;
  discountAmount: number;
  taxAmount: number;
  totalAmount: number;
  status: SalesOrderStatus;
  messages?: AsapMessage[];
}

/** How a receipt was paid for. */
export type TenderKind = 'Cash' | 'Card' | 'Voucher' | 'OnAccount';

/** Where a till session stands. */
export type PosSessionStatus = 'Open' | 'Closed';

/** A till. */
export interface PosStation {
  code: string;
  name: string;
  nameArabic?: string;
  locationCode: string;
  defaultCustomerNo: string;
  isBlocked: boolean;

  /** The session open on it, or null when nobody is trading. */
  openSessionNo: string | null;
}

/** One cashier's turn at one till. */
export interface PosSession {
  no: string;
  stationCode: string;
  cashierName?: string;
  openedAtUtc: string;
  businessDate: string;
  status: PosSessionStatus;
  openingFloat: number;
  cashTendered: number;
  changeGiven: number;
  cashRefunded: number;

  /** Taken by card, which never reaches the drawer. */
  cardTaken: number;
  onAccountTaken: number;
  netSales: number;
  taxAmount: number;
  grossSales: number;
  receiptCount: number;
  readingCount: number;

  /** What should be in the drawer: float plus cash in, less change and refunds out. */
  expectedCash: number;
  declaredCash: number | null;

  /** Counted less expected. Negative is short, positive is over, null while still open. */
  variance: number | null;
  closedAtUtc?: string;
  closingTransactionNo?: number;
}

/** One receipt as the session screen lists it. */
export interface PosReceiptSummary {
  no: string;
  customerName: string;
  netAmount: number;
  taxAmount: number;
  roundingAmount: number;
  costAmount: number;
  changeGiven: number;
  status: string;
  transactionNo?: number;
}

/** A session with the receipts taken against it. */
export interface PosSessionDetail {
  session: PosSession;
  receipts: PosReceiptSummary[];
}

/** What a session looks like at a moment in time, without closing it. */
export interface PosReading {
  sessionNo: string;
  stationCode: string;
  cashierName?: string;
  openedAtUtc: string;
  receiptCount: number;
  netSales: number;
  taxAmount: number;
  openingFloat: number;
  cashTendered: number;
  changeGiven: number;
  cashRefunded: number;
  cardTaken: number;
  onAccountTaken: number;
  expectedCash: number;

  /** Which reading this is. A till read four times before a short count is worth noticing. */
  readingNo: number;
}

/** What closing a session settled. */
export interface PosSessionClosed {
  sessionNo: string;
  expectedCash: number;
  declaredCash: number;
  variance: number;
  transactionNo?: number;
  reading: PosReading;
  messages?: AsapMessage[];
}

/** A sale set aside and not yet paid for. */
export interface ParkedSale {
  /** Its handle. Not a receipt number, because it is not a receipt until somebody pays. */
  no: string;
  parkedAs?: string;
  takenAtUtc: string;
  lineCount: number;
  netAmount: number;
  lines: {
    type: 'Item' | 'GlAccount';
    no: string;
    quantity: number;
    unitPrice: number;
    discountPercent: number;
    description?: string;
    taxCode?: string;
  }[];
}

/** What a receipt posted. */
export interface PosReceiptPosted {
  receiptNo: string;
  transactionNo: number;
  netAmount: number;
  discountAmount: number;
  taxAmount: number;
  roundingAmount: number;
  totalAmount: number;
  changeGiven: number;
  costAmount: number;
  messages?: AsapMessage[];
}

/** What shape an offer takes. */
export type OfferKind = 'Percentage' | 'AmountPerUnit' | 'BuyXGetY' | 'Threshold' | 'FixedPrice';

/** What an offer applies to. */
export type OfferScope = 'Item' | 'Category' | 'Everything';

/** What happens when more than one offer could apply. */
export type StackingRule = 'Stacks' | 'Exclusive' | 'Blocking';

/** One thing an offer applies to. */
export interface OfferTarget {
  itemNo?: string;
  categoryId?: string;
}

/** A reason to charge less than the price list says. */
export interface Offer {
  code: string;
  name: string;
  nameArabic?: string;
  kind: OfferKind;
  scope: OfferScope;
  value: number;
  buyQuantity: number;
  getQuantity: number;
  getDiscountPercent: number;
  startsOn: string;
  endsOn?: string;
  startsAt?: string;
  endsAt?: string;

  /** Which days it runs, as a bit per day of week, or null for every day. */
  daysOfWeek?: number | null;
  channels: string;
  branchId?: string;
  customerGroup?: string;
  couponCode?: string;
  stacking: StackingRule;
  priority: number;
  isActive: boolean;
  timesApplied: number;
  totalGivenAway: number;
  targets: OfferTarget[];
}

/** What a client sends to write an offer. */
export interface SaveOfferRequest {
  code: string;
  name: string;
  nameArabic?: string;
  kind: OfferKind;
  scope: OfferScope;
  value?: number;
  buyQuantity?: number;
  getQuantity?: number;
  getDiscountPercent?: number;
  startsOn: string;
  endsOn?: string;
  startsAt?: string;
  endsAt?: string;
  daysOfWeek?: number | null;
  channels?: string;
  branchId?: string;
  customerGroup?: string;
  couponCode?: string;
  stacking?: StackingRule;
  priority?: number;
  isActive?: boolean;
  targets?: OfferTarget[];
  overrideReason?: string;
}

/** What an offer would do to one item, at today's cost. */
export interface OfferMarginRow {
  itemNo: string;
  description: string;
  unitPrice: number;
  unitCost: number;
  offerPrice: number;
  marginPercent: number;

  /** How far under the floor, per unit. The figure a decision gets made on. */
  shortfallPerUnit: number;
  isAcceptable: boolean;
}

/** What an offer would do across everything it covers. */
export interface OfferPreview {
  floorPercent: number;

  /** The worst margin the offer would leave anywhere, or null when it covers nothing. */
  worst: number | null;

  /** How many items it would take below the floor. */
  breaches: number;
  rows: OfferMarginRow[];
}

/** What writing an offer produced. */
export interface OfferSaved {
  offer: Offer;
  messages?: AsapMessage[];
}

/** How one offer did over a period. */
export interface OfferUptakeRow {
  offerCode: string;
  receipts: number;
  units: number;
  givenAway: number;
  revenueAtList: number;
  netRevenue: number;
  costOfGoods: number;

  /** Null where any line counted had no cost recorded. Not the same as nothing. */
  grossProfit: number | null;

  /** What was left, as a percentage of what was charged, or null where the cost is not known. */
  realisedMarginPercent: number | null;

  /** How much of the shelf price was given away. */
  discountPercent: number;
}

/** What every offer did, and what the shop sold without one. */
export interface PromotionUptake {
  from: string;
  to: string;
  totalGivenAway: number;
  promotedNetRevenue: number;
  unpromotedNetRevenue: number;

  /**
   * The margin on everything sold at the ordinary price. Every offer is judged against this: a
   * promotion's margin on its own is a number nobody can interpret.
   */
  unpromotedMarginPercent: number | null;
  offers: OfferUptakeRow[];
}

/** A shop, warehouse or head office. Almost every document names one. */
export interface Branch {
  id: string;
  code: string;
  name: string;
  nameArabic: string | null;
  kind: string;
  city: string | null;
  address?: string | null;
  phone?: string | null;
  isActive: boolean;
}

/** The companies, and which one the caller is working in. */
export interface Companies {
  current: string | null;
  companies: Company[];
}

/** Where somebody worked, and when. Open-ended until they move. */
export interface BranchAssignment {
  branchId: string;
  fromDate: string;
  toDate: string | null;
  reason: string | null;
}

/** Somebody who works here, or used to. */
export interface Employee {
  no: string;
  name: string;
  nameArabic: string | null;
  nationality: string | null;
  hiredOn: string;
  leftOn: string | null;
  leavingReason: string;
  status: string;
  position: string | null;

  /** Null rather than zero where the caller may not see pay. Zero would read as "unpaid". */
  basicWage: number | null;
  allowances: number | null;
  totalWage: number | null;
  branchAssignments: BranchAssignment[];
}

/** Why somebody left, which decides what they are owed rather than describing it. */
export type LeavingReason =
  | 'None'
  | 'Resignation'
  | 'Termination'
  | 'EndOfContract'
  | 'Retirement'
  | 'Death'
  | 'Disability';

/** What a client sends to hire somebody. */
export interface HireRequest {
  name: string;
  hiredOn: string;
  nameArabic?: string | null;
  no?: string | null;
  nationalId?: string | null;
  nationality?: string | null;
  basicWage?: number;
  allowances?: number;
  branchId?: string | null;
}

/** What a client sends to move somebody to another branch. */
export interface TransferRequest {
  branchId: string;
  fromDate: string;
  reason?: string | null;
}

/** What a client sends to record that somebody has left. */
export interface LeavingRequest {
  leftOn: string;
  reason: LeavingReason;
}

/** An employee, and whatever was worth saying about the change. */
export interface EmployeeSaved {
  employee: Employee;
  messages: AsapMessage[];
}

/** What one person has earned and not yet been given. */
export interface EmployeeEntitlement {
  employeeNo: string;
  name: string;
  serviceYears: number;
  leaveDays: number;
  leaveLiability: number;
  endOfService: number;
  totalOwed: number;
}

/** What the company owes its staff, in total and per person. */
export interface Entitlements {
  totalOwed: number;
  leaveLiability: number;
  endOfService: number;
  employees: EmployeeEntitlement[];
}

/** How much of somebody's month one branch carries. */
export interface PayrollBranchShare {
  branchId: string;
  days: number;
  amount: number;
}

/** What one person is owed for the period, and where the cost lands. */
export interface PayrollLine {
  employeeNo: string;
  employeeName: string;
  daysWorked: number;
  basicPay: number;
  allowances: number;
  otherEarnings: number;
  deductions: number;

  /** What the deduction was for. A figure with nothing beside it is what somebody asks about. */
  note: string | null;
  grossPay: number;
  netPay: number;
  endOfServiceCharge: number;
  branchShares: PayrollBranchShare[];
}

/** A payroll run in the list. */
export interface PayrollRunSummary {
  no: string;
  fromDate: string;
  toDate: string;
  status: string;
  people: number;
  grossPay: number;
  netPay: number;
  endOfServiceCharge: number;
  transactionNo: number | null;
}

/** A payroll run and everybody in it. */
export interface PayrollRun extends PayrollRunSummary {
  postingDate: string;
  description: string | null;
  daysInPeriod: number;
  deductions: number;
  lines: PayrollLine[];
}

/** What a client sends to work out a period. */
export interface CalculatePayrollRequest {
  from: string;
  to: string;
  postingDate?: string | null;
  description?: string | null;
}

/** A run, and whatever was worth saying about it. */
export interface PayrollSaved {
  run: PayrollRun;
  messages: AsapMessage[];
}

/** What one branch earned and spent over a range. */
export interface BranchPerformanceRow {
  branchId: string | null;
  code: string | null;
  name: string;
  nameArabic: string | null;
  revenue: number;
  costOfSales: number;
  grossProfit: number;
  expenses: number;

  /** Broken out because it is the largest cost a shop manager is actually asked about. */
  staffCost: number;
  result: number;

  /** Null where nothing was sold, which is not the same as a margin of nil. */
  grossMarginPercent: number | null;
}

/** What every branch earned and spent, and what was charged to none of them. */
export interface BranchPerformance {
  from: string;
  to: string;
  currencyCode: string;
  branches: BranchPerformanceRow[];

  /**
   * What carries no branch. Shown rather than spread, so the reader knows how much of the
   * company's result the branch rows do not account for.
   */
  unattributed: BranchPerformanceRow | null;
  total: BranchPerformanceRow;
}

/** Why somebody is away. What it is called decides what it is paid at. */
export type LeaveKind =
  | 'Annual'
  | 'Sick'
  | 'Unpaid'
  | 'Maternity'
  | 'Hajj'
  | 'Marriage'
  | 'Bereavement'
  | 'Examination';

/** Where a leave request has got to. */
export type LeaveStatus = 'Draft' | 'Submitted' | 'Approved' | 'Rejected' | 'Cancelled';

/** Somebody asking to be away, and what was decided. */
export interface LeaveRequest {
  no: string;
  employeeNo: string;
  employeeName: string;
  kind: LeaveKind;
  fromDate: string;
  toDate: string;

  /** Calendar days, inclusive of both ends, which is how the law counts leave. */
  days: number;
  status: LeaveStatus;
  reason: string | null;
  decisionNote: string | null;
  decidedAtUtc: string | null;
}

/** What one employee has earned, taken and has left. */
export interface LeaveEntitlement {
  employeeNo: string;
  name: string;
  asAt: string;
  earnedDays: number;
  takenDays: number;

  /** Negative where leave was granted before it was earned, which is a real state. */
  balanceDays: number;
  liability: number;
}

/** What a client sends to ask for leave. */
export interface LeaveRequestInput {
  employeeNo: string;
  kind: LeaveKind;
  fromDate: string;
  toDate: string;
  reason?: string | null;
  submit?: boolean;
}

/** A request, and whatever was worth saying about it. */
export interface LeaveSaved {
  request: LeaveRequest;
  messages: AsapMessage[];
}

/** One permitted value of an option setting. */
export interface SettingOption {
  value: string;
  label: string;
}

/** One setting a module declares, and what it is currently set to. */
export interface Setting {
  key: string;
  module: string;
  group: string;
  displayName: string;

  /** What it actually does. Shown next to the input, not behind a help icon. */
  description: string;
  valueType: 'Text' | 'Number' | 'Boolean' | 'Date' | 'Option' | 'EntityReference' | string;
  scope: string;
  defaultValue: string | null;

  /** What is in force. Null means nobody has set it and the default applies. */
  value: string | null;
  isSet: boolean;
  referencedEntityType: string | null;
  allowedValues: SettingOption[];
  requiresPermission: string | null;

  /** Decided by the server, so the screen cannot disagree with the endpoint. */
  canChange: boolean;
  helpTopic: string | null;
}

/** A setting after it was changed. */
export interface SettingSaved {
  key: string;
  value: string | null;
  isSet: boolean;
  messages: AsapMessage[];
}

/** One permission a module declares, and what it means. */
export interface PermissionInfo {
  key: string;
  module: string;
  resource: string;
  action: string;
  displayName: string;
  description: string | null;

  /** Flagged rather than hidden: hiding it would only mean granting it blind. */
  isSensitive: boolean;
  implies: string[];
}

/** A user account. */
export interface UserAccount {
  userName: string;
  displayName: string;
  email: string | null;
  isActive: boolean;
  isSuperUser: boolean;

  /** True while the password is one somebody else chose and still knows. */
  mustChangePassword: boolean;
  culture: string | null;
  lastLoginAtUtc: string | null;
  lockedUntilUtc: string | null;
  permissionSets: string[];
}

/** A permission set and everything it grants. */
export interface PermissionSetInfo {
  code: string;
  name: string;
  nameArabic: string | null;
  description: string | null;

  /** ASAP keeps this one in step with the modules, so it cannot be edited. */
  isSystemDefined: boolean;
  assignedTo: number;
  permissions: string[];
}

/** One thing somebody did, as the audit log recorded it. */
export interface AuditEntry {
  occurredAtUtc: string;
  userName: string | null;
  action: string;
  entityType: string | null;
  displayNo: string | null;
  changes: string | null;

  /** The protection that was pushed past, where one was. */
  overriddenMessageCode: string | null;

  /** Why. The column the whole screen exists for. */
  overrideReason: string | null;
  ipAddress: string | null;
  clientKind: string | null;
}

/** A page of the audit log, and the cap that produced it. */
export interface AuditPage {
  limit: number;
  rows: AuditEntry[];
}

/** One dated range of numbers a series issues from. */
export interface NumberSeriesLineInfo {
  startingDate: string;
  startingNumber: string;
  endingNumber: string | null;
  lastNumberUsed: string | null;
  lastDateUsed: string | null;
  increment: number;
  warnWhenRemainingBelow: number | null;
  isOpen: boolean;

  /** How many are left, or null where the line has no ceiling. */
  remaining: number | null;
}

/** A series every document number of one kind comes out of. */
export interface NumberSeriesInfo {
  code: string;
  description: string;
  descriptionArabic: string | null;

  /** Off means gapless, which a tax invoice sequence has to be. */
  allowGaps: boolean;
  allowManualEntry: boolean;
  enforceDateOrder: boolean;
  isActive: boolean;
  lines: NumberSeriesLineInfo[];
}

/** One help topic in the index. */
export interface HelpTopicSummary {
  topic: string;

  /** The module or area it belongs to, which is how the index is grouped. */
  area: string;
  title: string;
}

/** One help topic, as written. */
export interface HelpPage {
  topic: string;

  /** The language it actually came back in. */
  language: string;

  /** The language that was asked for. Different means it has not been translated yet. */
  requestedLanguage: string;
  title: string;
  markdown: string;
}

/** One item on a count sheet. */
export interface StockCountLine {
  itemNo: string;
  description: string;

  /** What the system said when the sheet was made. Frozen, so the comparison is stable. */
  systemQuantity: number;

  /** What was found, or null where nobody has looked. Nought and null are different states. */
  countedQuantity: number | null;
  difference: number;
  note: string | null;
}

/** A stock count in the list. */
export interface StockCountSummary {
  no: string;
  locationCode: string;
  countDate: string;
  status: string;
  description: string | null;
  lines: number;
  notCounted: number;
  differences: number;
  transactionNo: number | null;
}

/** A stock count and its sheet. */
export interface StockCount {
  no: string;
  locationCode: string;
  countDate: string;
  status: string;
  description: string | null;

  /** When the system quantities were taken. */
  sheetTakenAtUtc: string;
  notCounted: number;
  transactionNo: number | null;
  lines: StockCountLine[];
}

/** A posted count, and whatever was worth saying about it. */
export interface StockCountPosted {
  count: StockCount;
  messages: AsapMessage[];
}

/** A layout a shop manager can edit without a developer. */
export interface PrintTemplate {
  code: string;
  name: string;
  nameArabic: string | null;
  kind: string;
  content: string;

  /** How many characters wide the paper is. Forty-two is the usual eighty-millimetre roll. */
  widthInCharacters: number;
  branchId: string | null;
  isDefault: boolean;
  isActive: boolean;
}

/** One field a template may refer to, and the region it belongs to. */
export interface PrintTemplateField {
  /** Empty for a field of the whole document. */
  region: string;
  field: string;
}

/** The templates, and what they may refer to. */
export interface PrintTemplates {
  templates: PrintTemplate[];
  fields: PrintTemplateField[];
}

/** A rendered document, ready to go to paper. */
export interface PrintedDocument {
  templateCode: string;
  widthInCharacters: number;
  text: string;
}

/** An unsaved template rendered against a real receipt. */
export interface PrintPreview {
  text: string;
  widthInCharacters: number;

  /** Which receipt it was rendered against, or null where the shop has never sold anything. */
  receiptNo: string | null;
}

/** One module as the reference sees it. */
export interface ReferenceModule {
  moduleId: string;
  displayName: string;
  description: string;
  version: string;
  dependsOn: string[];
  messages: number;
  permissions: number;
  settings: number;
  menuEntries: number;
}

/** What the installation declares, in total. */
export interface ReferenceSummary {
  modules: ReferenceModule[];
  platform: {
    messages: number;
    totalMessages: number;
    byPrefix: { prefix: string; count: number }[];
  };
}

/** Everything one module declares. */
export interface ModuleReference {
  moduleId: string;
  displayName: string;
  description: string;
  version: string;
  dependsOn: string[];
  permissions: {
    key: string;
    displayName: string;
    description: string | null;
    isSensitive: boolean;
    implies: string[];
  }[];
  settings: {
    key: string;
    displayName: string;
    description: string;
    valueType: string;
    scope: string;
    defaultValue: string | null;
    requiresPermission: string | null;
    helpTopic: string | null;
  }[];
  messages: {
    code: string;
    severity: string;
    title: string;

    /** The contract between a message and whatever raises it. */
    placeholders: string[];
    overridePermission: string | null;
    helpTopic: string | null;
  }[];
  menu: {
    id: string;
    displayName: string;
    kind: string;
    route: string | null;
    requiresPermission: string | null;
  }[];
}

/** One domain event an extension can subscribe to. */
export interface ReferenceEvent {
  type: string;
  assembly: string;

  /** Whether an extension can stop it, or is only being told it happened. */
  isVetoable: boolean;
  properties: { name: string; type: string }[];
}

/** A currency the company transacts in, and what it is worth today. */
export interface CurrencyInfo {
  code: string;
  name: string;
  nameArabic: string | null;
  symbol: string | null;
  decimalPlaces: number;
  isActive: boolean;

  /**
   * What one unit is worth today, or null when today has no rate. For reading only — a posting
   * resolves the rate from its own document date, never from this.
   */
  rate: number | null;
  rateStartingOn: string | null;
}

/** One dated exchange rate. */
export interface ExchangeRateInfo {
  startingDate: string;

  /** How many units the pair is quoted for, usually one. */
  currencyAmount: number;
  baseAmount: number;
  multiplier: number;
}

/** A bank account the company holds. */
export interface BankAccountInfo {
  id: string;
  code: string;
  name: string;
  nameArabic: string | null;
  bankName: string | null;
  iban: string | null;
  glAccountNo: string;
  currencyCode: string | null;
  isActive: boolean;
}

/** A statement, without its lines. */
export interface BankStatementInfo {
  id: string;
  no: string;
  statementDate: string;
  openingBalance: number;
  closingBalance: number;
  status: string;
  reconciledOn: string | null;
  lineCount: number;
  unmatchedLines: number;
}

/** One line of a statement, and what in the ledger it turned out to be. */
export interface BankStatementLineInfo {
  id: string;
  transactionDate: string;
  description: string;
  reference: string | null;
  amount: number;
  matchedEntryId: string | null;
  note: string | null;
}

/** One ledger entry the bank has not seen yet. */
export interface OutstandingItemInfo {
  entryId: string;
  postingDate: string;
  documentNo: string | null;
  description: string;
  amount: number;
}

/** Where a reconciliation stands, in the form an accountant would write it out. */
export interface ReconciliationPositionInfo {
  statementNo: string;
  statementDate: string;
  closingBalance: number;
  ledgerBalance: number;
  outstandingTotal: number;

  /** What is left unexplained. Nought is the only value that proves anything. */
  difference: number;
  unmatchedLines: number;
  balances: boolean;
  outstanding: OutstandingItemInfo[];
}

/** A statement with everything needed to work on it. */
export interface BankStatementDetail {
  statement: BankStatementInfo;
  lines: BankStatementLineInfo[];
  position: ReconciliationPositionInfo;
}

/** A statement layout as it appears in the list. */
export interface ScheduleSummary {
  code: string;
  name: string;
  nameArabic: string | null;
  description: string | null;
  rows: number;
}

/** One row of a statement layout, as it is edited. */
export interface ScheduleLine {
  rowNo: string;
  description: string;
  kind: 'Accounts' | 'Formula' | 'Heading';

  /** The account range, or the formula, depending on the kind. */
  expression: string | null;
  descriptionArabic: string | null;
  amountKind: 'NetChange' | 'BalanceAtDate';

  /** Applied before formulas run, so a formula means what it looks like. */
  showOppositeSign: boolean;
  indent: number;
  isBold: boolean;
  hideIfZero: boolean;
}

/** A whole layout, for editing. */
export interface ScheduleLayout {
  code: string;
  name: string;
  nameArabic: string | null;
  description: string | null;
  isActive: boolean;
  lines: ScheduleLine[];
}

/** One row of a layout that has been run. */
export interface ScheduleReportRow {
  rowNo: string;
  description: string;
  descriptionArabic: string | null;

  /** Null when the figure has no answer, such as a margin on a month with no revenue. */
  amount: number | null;
  indent: number;
  isBold: boolean;
  isHeading: boolean;
}

/** A statement, run. */
export interface ScheduleReport {
  code: string;
  name: string;
  nameArabic: string | null;
  from: string;
  to: string;
  currencyCode: string;
  rows: ScheduleReportRow[];
}

/** How many people are at one branch. */
export interface HeadcountRow {
  branchId: string | null;
  branchCode: string | null;
  branchName: string | null;
  count: number;
}

/** What one branch's staff cost, at contractual rates. */
export interface BranchCostRow extends HeadcountRow {
  monthlyWageCost: number;
}

/** How many people came and went over a period. */
export interface Turnover {
  fromDate: string;
  toDate: string;
  openingHeadcount: number;
  hired: number;
  left: number;
  closingHeadcount: number;

  /** Leavers against the average of the opening and closing headcounts, not against either end. */
  turnoverRate: number;
}

/** One value a dimension may take. */
export interface DimensionValueRow {
  code: string;
  name: string;
  nameArabic: string | null;
  kind: string;
  totalRange: string | null;
  indentation: number;
  isBlocked: boolean;
}

/** An axis the company analyses its figures along. */
export interface DimensionRow {
  code: string;
  name: string;
  nameArabic: string | null;
  description: string | null;
  shortcutIndex: number | null;
  isMandatory: boolean;
  isBlocked: boolean;
  values: DimensionValueRow[];
}

/** One period of a financial year. */
export interface FiscalPeriodRow {
  name: string;
  startDate: string;
  endDate: string;
  isClosed: boolean;
}

/** A financial year and its periods. */
export interface FiscalYearRow {
  code: string;
  startDate: string;
  endDate: string;
  isClosed: boolean;
  incomeTransferred: boolean;
  closedAtUtc: string | null;
  periods: FiscalPeriodRow[];
}

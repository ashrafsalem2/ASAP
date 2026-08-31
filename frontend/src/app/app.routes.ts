import { Routes } from '@angular/router';
import { authGuard, requirePermission } from './core/auth/auth.guard';

/**
 * Every screen is lazily loaded.
 *
 * An ERP grows to hundreds of screens, and a customer who bought Finance should not download
 * Payroll to look at a trial balance. Loading each on demand keeps the first paint the same size
 * whatever is installed.
 *
 * Routes are guarded by permission as well as hidden from the menu. The menu already omits what a
 * user cannot open, but an address can be typed and a bookmark can outlive a change of role, and a
 * screen that loads and then fails every request is worse than one that is simply not there.
 */
export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/login/login').then((m) => m.Login),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./shell/shell').then((m) => m.Shell),
    children: [
      {
        path: '',
        loadComponent: () => import('./features/home/home').then((m) => m.Home),
      },
      {
        path: 'finance/accounts',
        canActivate: [requirePermission('Finance.Account.Read')],
        loadComponent: () =>
          import('./features/finance/chart-of-accounts').then((m) => m.ChartOfAccounts),
      },
      {
        path: 'admin/dimensions',
        canActivate: [requirePermission('Platform.Dimension.Read')],
        loadComponent: () => import('./features/admin/dimensions').then((m) => m.Dimensions),
      },
      {
        path: 'finance/periods',
        canActivate: [requirePermission('Finance.Period.Read')],
        loadComponent: () => import('./features/finance/periods').then((m) => m.FiscalPeriods),
      },
      {
        path: 'inventory/variants',
        canActivate: [requirePermission('Inventory.Variant.Read')],
        loadComponent: () => import('./features/inventory/variants').then((m) => m.Variants),
      },
      {
        path: 'inventory/categories',
        canActivate: [requirePermission('Inventory.Category.Read')],
        loadComponent: () => import('./features/inventory/categories').then((m) => m.Categories),
      },
      {
        path: 'inventory/adjustment-reasons',
        canActivate: [requirePermission('Inventory.AdjustmentReason.Read')],
        loadComponent: () =>
          import('./features/inventory/adjustment-reasons').then((m) => m.AdjustmentReasons),
      },
      {
        path: 'inventory/reports/analysis',
        canActivate: [requirePermission('Inventory.Report.Read')],
        loadComponent: () =>
          import('./features/inventory/stock-analysis').then((m) => m.StockAnalysis),
      },
      {
        path: 'inventory/bin-movements',
        canActivate: [requirePermission('Inventory.Item.Read')],
        loadComponent: () =>
          import('./features/inventory/bin-movements').then((m) => m.BinMovements),
      },
      {
        path: 'inventory/reorder-policies',
        canActivate: [requirePermission('Inventory.Item.Read')],
        loadComponent: () =>
          import('./features/inventory/reorder-policies').then((m) => m.ReorderPolicies),
      },
      {
        path: 'inventory/reservations',
        canActivate: [requirePermission('Inventory.Reservation.Read')],
        loadComponent: () =>
          import('./features/inventory/reservations').then((m) => m.Reservations),
      },
      {
        path: 'inventory/bins',
        canActivate: [requirePermission('Inventory.Bin.Read')],
        loadComponent: () => import('./features/inventory/bins').then((m) => m.Bins),
      },
      {
        path: 'inventory/units',
        canActivate: [requirePermission('Inventory.Unit.Read')],
        loadComponent: () => import('./features/inventory/units').then((m) => m.Units),
      },
      {
        path: 'inventory/locations',
        canActivate: [requirePermission('Inventory.Location.Read')],
        loadComponent: () => import('./features/inventory/locations').then((m) => m.Locations),
      },
      {
        path: 'pos/stations',
        canActivate: [requirePermission('Pos.Station.Read')],
        loadComponent: () => import('./features/pos/stations').then((m) => m.Stations),
      },
      {
        path: 'hr/reports/headcount',
        canActivate: [requirePermission('Hr.Report.Read')],
        loadComponent: () => import('./features/hr/headcount').then((m) => m.Headcount),
      },
      {
        path: 'hr/reports/cost-by-branch',
        canActivate: [requirePermission('Hr.Report.Read')],
        loadComponent: () => import('./features/hr/cost-by-branch').then((m) => m.CostByBranch),
      },
      {
        path: 'hr/reports/turnover',
        canActivate: [requirePermission('Hr.Report.Read')],
        loadComponent: () => import('./features/hr/turnover').then((m) => m.TurnoverReport),
      },
      {
        path: 'finance/recurring',
        canActivate: [requirePermission('Finance.Journal.Read')],
        loadComponent: () => import('./features/finance/recurring').then((m) => m.Recurring),
      },
      {
        path: 'finance/schedules',
        canActivate: [requirePermission('Finance.Report.Read')],
        loadComponent: () => import('./features/finance/schedules').then((m) => m.Schedules),
      },
      {
        path: 'finance/bank-reconciliation',
        canActivate: [requirePermission('Finance.Bank.Read')],
        loadComponent: () =>
          import('./features/finance/bank-reconciliation').then((m) => m.BankReconciliation),
      },
      {
        path: 'finance/customer-groups',
        canActivate: [requirePermission('Finance.Party.Read')],
        loadComponent: () =>
          import('./features/finance/customer-groups').then((m) => m.CustomerGroups),
      },
      {
        path: 'finance/currencies',
        canActivate: [requirePermission('Finance.Currency.Read')],
        loadComponent: () => import('./features/finance/currencies').then((m) => m.Currencies),
      },
      {
        path: 'finance/journals',
        canActivate: [requirePermission('Finance.Journal.Read')],
        loadComponent: () => import('./features/finance/journal').then((m) => m.Journal),
      },
      {
        path: 'finance/entries',
        canActivate: [requirePermission('Finance.Entry.Read')],
        loadComponent: () =>
          import('./features/finance/ledger-entries').then((m) => m.LedgerEntries),
      },
      {
        path: 'inventory/items',
        canActivate: [requirePermission('Inventory.Item.Read')],
        loadComponent: () => import('./features/inventory/items').then((m) => m.Items),
      },
      {
        path: 'inventory/movements',
        canActivate: [requirePermission('Inventory.Stock.Read')],
        loadComponent: () =>
          import('./features/inventory/stock-movements').then((m) => m.StockMovements),
      },
      {
        path: 'finance/customers',
        canActivate: [requirePermission('Finance.Party.Read')],
        data: { kind: 'Customer' },
        loadComponent: () => import('./features/finance/parties').then((m) => m.Parties),
      },
      {
        path: 'finance/customers/:partyNo',
        canActivate: [requirePermission('Finance.Party.Read')],
        data: { kind: 'Customer' },
        loadComponent: () => import('./features/finance/party-ledger').then((m) => m.PartyLedger),
      },
      {
        path: 'finance/vendors',
        canActivate: [requirePermission('Finance.Party.Read')],
        data: { kind: 'Vendor' },
        loadComponent: () => import('./features/finance/parties').then((m) => m.Parties),
      },
      {
        path: 'finance/vendors/:partyNo',
        canActivate: [requirePermission('Finance.Party.Read')],
        data: { kind: 'Vendor' },
        loadComponent: () => import('./features/finance/party-ledger').then((m) => m.PartyLedger),
      },
      {
        path: 'finance/reports/tax-return',
        canActivate: [requirePermission('Finance.Report.Read')],
        loadComponent: () =>
          import('./features/finance/tax-return').then((m) => m.TaxReturnReport),
      },
      {
        path: 'finance/reports/aged-analysis',
        canActivate: [requirePermission('Finance.Report.Read')],
        loadComponent: () =>
          import('./features/finance/aged-analysis').then((m) => m.AgedAnalysisReport),
      },
      {
        path: 'finance/reports/income-statement',
        canActivate: [requirePermission('Finance.Report.Read')],
        loadComponent: () =>
          import('./features/finance/income-statement').then((m) => m.IncomeStatementReport),
      },
      {
        path: 'finance/reports/balance-sheet',
        canActivate: [requirePermission('Finance.Report.Read')],
        loadComponent: () =>
          import('./features/finance/balance-sheet').then((m) => m.BalanceSheetReport),
      },
      {
        path: 'purchasing/reports',
        canActivate: [requirePermission('Purchasing.Order.Read')],
        loadComponent: () =>
          import('./features/purchasing/reports').then((m) => m.PurchaseReports),
      },
      {
        path: 'purchasing/approval-limits',
        canActivate: [requirePermission('Purchasing.Approval.Read')],
        loadComponent: () =>
          import('./features/purchasing/approval-limits').then((m) => m.ApprovalLimits),
      },
      {
        path: 'purchasing/quotations',
        canActivate: [requirePermission('Purchasing.Quotation.Read')],
        loadComponent: () =>
          import('./features/purchasing/quotations').then((m) => m.Quotations),
      },
      {
        path: 'purchasing/replenishment',
        canActivate: [requirePermission('Purchasing.Requisition.Read')],
        loadComponent: () =>
          import('./features/purchasing/replenishment').then((m) => m.Replenishment),
      },
      {
        path: 'purchasing/requisitions',
        canActivate: [requirePermission('Purchasing.Requisition.Read')],
        loadComponent: () =>
          import('./features/purchasing/requisitions').then((m) => m.Requisitions),
      },
      {
        path: 'purchasing/orders',
        canActivate: [requirePermission('Purchasing.Order.Read')],
        loadComponent: () =>
          import('./features/purchasing/purchase-orders').then((m) => m.PurchaseOrders),
      },
      {
        path: 'purchasing/orders/:orderNo',
        canActivate: [requirePermission('Purchasing.Order.Read')],
        loadComponent: () =>
          import('./features/purchasing/purchase-order').then((m) => m.PurchaseOrderDetail),
      },
      {
        path: 'sales/quotes',
        canActivate: [requirePermission('Sales.Quote.Read')],
        loadComponent: () => import('./features/sales/quotes').then((m) => m.Quotes),
      },
      {
        path: 'sales/price-lists',
        canActivate: [requirePermission('Sales.PriceList.Read')],
        loadComponent: () => import('./features/sales/price-lists').then((m) => m.PriceLists),
      },
      {
        path: 'sales/reports',
        canActivate: [requirePermission('Sales.Order.Read')],
        loadComponent: () => import('./features/sales/reports').then((m) => m.SalesReports),
      },
      {
        path: 'sales/orders',
        canActivate: [requirePermission('Sales.Order.Read')],
        loadComponent: () => import('./features/sales/sales-orders').then((m) => m.SalesOrders),
      },
      {
        path: 'sales/orders/:orderNo',
        canActivate: [requirePermission('Sales.Order.Read')],
        loadComponent: () =>
          import('./features/sales/sales-order').then((m) => m.SalesOrderDetail),
      },
      {
        path: 'pos/till',
        canActivate: [requirePermission('Pos.Receipt.Read')],
        loadComponent: () => import('./features/pos/till').then((m) => m.Till),
      },
      {
        path: 'pos/print-templates',
        canActivate: [requirePermission('Pos.Station.Read')],
        loadComponent: () =>
          import('./features/pos/print-templates').then((m) => m.PrintTemplates),
      },
      {
        path: 'pos/sessions',
        canActivate: [requirePermission('Pos.Session.Read')],
        loadComponent: () => import('./features/pos/sessions').then((m) => m.PosSessions),
      },
      {
        path: 'promotions/reports',
        canActivate: [requirePermission('Promotions.Offer.Read')],
        loadComponent: () => import('./features/promotions/reports').then((m) => m.PromotionReports),
      },
      {
        path: 'promotions/offers',
        canActivate: [requirePermission('Promotions.Offer.Read')],
        loadComponent: () => import('./features/promotions/offers').then((m) => m.Offers),
      },
      {
        path: 'pos/promotions',
        canActivate: [requirePermission('Pos.Report.Read')],
        loadComponent: () =>
          import('./features/pos/promotion-uptake').then((m) => m.PromotionUptakeReport),
      },
      {
        path: 'inventory/counts',
        canActivate: [requirePermission('Inventory.Count.Read')],
        loadComponent: () =>
          import('./features/inventory/stock-counts').then((m) => m.StockCounts),
      },
      {
        path: 'inventory/transfers',
        canActivate: [requirePermission('Inventory.Transfer.Read')],
        loadComponent: () => import('./features/inventory/transfers').then((m) => m.Transfers),
      },
      {
        path: 'inventory/reports/stock-on-hand',
        canActivate: [requirePermission('Inventory.Report.Read')],
        loadComponent: () =>
          import('./features/inventory/stock-on-hand').then((m) => m.StockOnHand),
      },
      {
        path: 'finance/reports/trial-balance',
        canActivate: [requirePermission('Finance.Report.Read')],
        loadComponent: () =>
          import('./features/finance/trial-balance').then((m) => m.TrialBalance),
      },
      {
        path: 'finance/reports/branch-performance',
        canActivate: [requirePermission('Finance.Report.Read')],
        loadComponent: () =>
          import('./features/finance/branch-performance').then((m) => m.BranchPerformanceReport),
      },
      {
        path: 'hr/attendance',
        canActivate: [requirePermission('Hr.Employee.Read')],
        loadComponent: () => import('./features/hr/attendance').then((m) => m.Attendance),
      },
      {
        path: 'hr/contracts',
        canActivate: [requirePermission('Hr.Employee.Read')],
        loadComponent: () =>
          import('./features/hr/contracts').then((m) => m.EmploymentContracts),
      },
      {
        path: 'hr/employees',
        canActivate: [requirePermission('Hr.Employee.Read')],
        loadComponent: () => import('./features/hr/employees').then((m) => m.Employees),
      },
      {
        path: 'hr/leave',
        canActivate: [requirePermission('Hr.Leave.Read')],
        loadComponent: () => import('./features/hr/leave').then((m) => m.Leave),
      },
      {
        path: 'hr/payroll',
        canActivate: [requirePermission('Hr.Wage.Read')],
        loadComponent: () => import('./features/hr/payroll').then((m) => m.Payroll),
      },
      {
        path: 'hr/entitlements',
        canActivate: [requirePermission('Hr.Report.Read')],
        loadComponent: () => import('./features/hr/entitlements').then((m) => m.Entitlements),
      },
      {
        path: 'admin/number-series',
        canActivate: [requirePermission('Platform.NumberSeries.Read')],
        loadComponent: () =>
          import('./features/admin/number-series').then((m) => m.NumberSeriesScreen),
      },
      {
        path: 'admin/companies',
        canActivate: [requirePermission('Platform.Company.Read')],
        data: { kind: 'companies' },
        loadComponent: () => import('./features/admin/organisation').then((m) => m.Organisation),
      },
      {
        path: 'admin/branches',
        canActivate: [requirePermission('Platform.Branch.Read')],
        data: { kind: 'branches' },
        loadComponent: () => import('./features/admin/organisation').then((m) => m.Organisation),
      },
      {
        path: 'admin/reference',
        loadComponent: () => import('./features/admin/reference').then((m) => m.Reference),
      },
      {
        path: 'admin/audit-log',
        canActivate: [requirePermission('Platform.AuditLog.Read')],
        loadComponent: () => import('./features/admin/audit-log').then((m) => m.AuditLog),
      },
      {
        path: 'admin/users',
        canActivate: [requirePermission('Platform.User.Read')],
        loadComponent: () => import('./features/admin/users').then((m) => m.Users),
      },
      {
        path: 'admin/permission-sets',
        canActivate: [requirePermission('Platform.PermissionSet.Read')],
        loadComponent: () =>
          import('./features/admin/permission-sets').then((m) => m.PermissionSets),
      },
      {
        path: 'account/password',
        loadComponent: () => import('./features/admin/change-password').then((m) => m.ChangePassword),
      },
      {
        path: 'admin/setup',
        canActivate: [requirePermission('Platform.Setup.Read')],
        loadComponent: () => import('./features/setup/setup').then((m) => m.Setup),
      },
      {
        path: 'help',
        loadComponent: () => import('./features/help/help').then((m) => m.Help),
      },
      {
        path: 'help/:topic',
        loadComponent: () => import('./features/help/help').then((m) => m.Help),
      },
      {
        path: '**',
        redirectTo: '',
      },
    ],
  },
];


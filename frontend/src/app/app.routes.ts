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
        path: '**',
        redirectTo: '',
      },
    ],
  },
];

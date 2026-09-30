import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./features/dashboard/dashboard-page').then((m) => m.DashboardPage) },
  { path: 'wallets', loadComponent: () => import('./features/wallets/wallets-page').then((m) => m.WalletsPage) },
  {
    path: 'transactions',
    loadComponent: () => import('./features/transactions/transactions-page').then((m) => m.TransactionsPage),
  },
  { path: 'debts', loadComponent: () => import('./features/debts/debts-page').then((m) => m.DebtsPage) },
  { path: 'groups', loadComponent: () => import('./features/groups/groups-page').then((m) => m.GroupsPage) },
  {
    path: 'groups/:id',
    loadComponent: () => import('./features/groups/group-detail-page').then((m) => m.GroupDetailPage),
  },
  { path: 'contacts', loadComponent: () => import('./features/contacts/contacts-page').then((m) => m.ContactsPage) },
  {
    path: 'categories',
    loadComponent: () => import('./features/categories/categories-page').then((m) => m.CategoriesPage),
  },
  { path: 'pending', loadComponent: () => import('./features/pending/pending-page').then((m) => m.PendingPage) },
  { path: 'changes', loadComponent: () => import('./features/changes/changes-page').then((m) => m.ChangesPage) },
  { path: 'activity', loadComponent: () => import('./features/activity/activity-page').then((m) => m.ActivityPage) },
  { path: 'settings', loadComponent: () => import('./features/settings/settings-page').then((m) => m.SettingsPage) },
  { path: '**', redirectTo: '' },
];

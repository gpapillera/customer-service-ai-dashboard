import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';
import { customerAuthGuard } from './customer/customer-auth.guard';

// Route components are lazy-loaded via `loadComponent` so the initial bundle
// stays under the Angular budget; shared shells (LayoutComponent) and the
// Material/CDK/chart.js dependencies are split into the chunks that use them
// automatically by the build. Guards stay eager (they are tiny).
// See docs/DIY.md §11 for how this tree wraps guarded routes in LayoutComponent.
export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./auth/login/login.component').then(m => m.LoginComponent) },
  { path: 'reset-password', loadComponent: () => import('./auth/reset-password.component').then(m => m.ResetPasswordComponent) },
  {
    path: '',
    loadComponent: () => import('./shared/layout/layout.component').then(m => m.LayoutComponent),
    canActivate: [authGuard],
    children: [
      { path: 'dashboard', loadComponent: () => import('./dashboard/dashboard.component').then(m => m.DashboardComponent) },
      {
        path: 'customers',
        children: [
          { path: '', loadComponent: () => import('./customers/customer-list.component').then(m => m.CustomerListComponent) },
          { path: ':id', loadComponent: () => import('./customers/customer-detail.component').then(m => m.CustomerDetailComponent) },
        ],
      },
      {
        path: 'cases',
        children: [
          { path: '', loadComponent: () => import('./cases/case-list.component').then(m => m.CaseListComponent) },
          { path: 'new', loadComponent: () => import('./cases/case-list.component').then(m => m.CaseListComponent) },
          { path: ':id', loadComponent: () => import('./cases/case-detail.component').then(m => m.CaseDetailComponent) },
          { path: ':id/edit', loadComponent: () => import('./cases/case-list.component').then(m => m.CaseListComponent) },
        ],
      },
      { path: 'agents', loadComponent: () => import('./users/agent-list.component').then(m => m.AgentListComponent) },
      { path: 'messages', loadComponent: () => import('./cases/conversations-list.component').then(m => m.ConversationsListComponent) },
      { path: 'conversations', loadComponent: () => import('./cases/admin-conversations.component').then(m => m.AdminConversationsComponent) },
      { path: 'emails', loadComponent: () => import('./email/email-list.component').then(m => m.EmailListComponent) },
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
    ],
  },
  // ---- Customer portal ----
  { path: 'customer/login', loadComponent: () => import('./customer/customer-login.component').then(m => m.CustomerLoginComponent) },
  { path: 'customer/accept-invite', loadComponent: () => import('./customer/accept-invite.component').then(m => m.AcceptInviteComponent) },
  {
    path: 'customer',
    loadComponent: () => import('./customer/customer-layout.component').then(m => m.CustomerLayoutComponent),
    canActivate: [customerAuthGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'cases' },
      { path: 'cases', loadComponent: () => import('./customer/my-cases-list.component').then(m => m.MyCasesListComponent) },
      { path: 'cases/new', loadComponent: () => import('./customer/new-case.component').then(m => m.NewCaseComponent) },
      { path: 'cases/:id', loadComponent: () => import('./customer/my-case-detail.component').then(m => m.MyCaseDetailComponent) },
    ],
  },
  { path: '**', redirectTo: 'dashboard' },
];

import { Routes } from '@angular/router';
import { authGuard } from './auth/auth.guard';
// See docs/DIY.md §11 for how this route tree wraps guarded routes in LayoutComponent.
import { LayoutComponent } from './shared/layout/layout.component';
import { LoginComponent } from './auth/login/login.component';
import { DashboardComponent } from './dashboard/dashboard.component';
import { CustomerListComponent } from './customers/customer-list.component';
import { CustomerDetailComponent } from './customers/customer-detail.component';
import { CaseListComponent } from './cases/case-list.component';
import { CaseDetailComponent } from './cases/case-detail.component';
import { CaseFormComponent } from './cases/case-form.component';
import { AgentListComponent } from './users/agent-list.component';

// Customer portal (Phase 3) — a separate shell, separate auth, separate routes.
import { customerAuthGuard } from './customer/customer-auth.guard';
import { CustomerLayoutComponent } from './customer/customer-layout.component';
import { CustomerLoginComponent } from './customer/customer-login.component';
import { AcceptInviteComponent } from './customer/accept-invite.component';
import { MyCasesListComponent } from './customer/my-cases-list.component';
import { NewCaseComponent } from './customer/new-case.component';
import { MyCaseDetailComponent } from './customer/my-case-detail.component';

export const routes: Routes = [
  { path: 'login', component: LoginComponent },
  {
    path: '',
    component: LayoutComponent,
    canActivate: [authGuard],
    children: [
      { path: 'dashboard', component: DashboardComponent },
      {
        path: 'customers',
        children: [
          { path: '', component: CustomerListComponent },
          { path: ':id', component: CustomerDetailComponent },
        ],
      },
      {
        path: 'cases',
        children: [
          { path: '', component: CaseListComponent },
          { path: 'new', component: CaseListComponent },
          { path: ':id', component: CaseDetailComponent },
          { path: ':id/edit', component: CaseListComponent },
        ],
      },
      { path: 'agents', component: AgentListComponent },
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
    ],
  },
  // ---- Customer portal ----
  { path: 'customer/login', component: CustomerLoginComponent },
  { path: 'customer/accept-invite', component: AcceptInviteComponent },
  {
    path: 'customer',
    component: CustomerLayoutComponent,
    canActivate: [customerAuthGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'cases' },
      { path: 'cases', component: MyCasesListComponent },
      { path: 'cases/new', component: NewCaseComponent },
      { path: 'cases/:id', component: MyCaseDetailComponent },
    ],
  },
  { path: '**', redirectTo: 'dashboard' },
];

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
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
    ],
  },
  { path: '**', redirectTo: 'dashboard' },
];

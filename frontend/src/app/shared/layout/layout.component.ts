import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { CsIconComponent } from '../cs-icon.component';
import { AuthService } from '../../auth/auth.service';

/**
 * Application shell: a Material sidenav with navigation plus a top toolbar
 * showing the current user and a logout action. All guarded routes render
 * inside the <router-outlet>.
 */
@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [
    CommonModule,
    RouterOutlet,
    RouterLink,
    MatSidenavModule,
    MatListModule,
    MatToolbarModule,
    MatIconModule,
    MatButtonModule,
    MatMenuModule,
    CsIconComponent,
  ],
  templateUrl: './layout.component.html',
  styleUrl: './layout.component.scss',
})
export class LayoutComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly navLinks = [
    { path: '/dashboard', label: 'Dashboard', icon: 'dashboard' },
    { path: '/customers', label: 'Customers', icon: 'people' },
    { path: '/cases', label: 'Cases', icon: 'confirmation_number' },
  ];

  /** The currently signed-in user (or null). */
  get user() {
    return this.auth.currentUser();
  }

  /** Logs the user out and returns to the login screen. */
  logout(): void {
    this.auth.logout();
    this.router.navigateByUrl('/login');
  }
}

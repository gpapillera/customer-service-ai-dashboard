import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog } from '@angular/material/dialog';
import { CsIconComponent } from '../cs-icon.component';
import { AuthService } from '../../auth/auth.service';
import { ConfirmDialogComponent } from '../confirm-dialog.component';

/**
 * Application shell: a white sidenav with navigation (active = light indigo
 * pill) and a Sign Out action pinned at the bottom. All guarded routes render
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
    MatIconModule,
    CsIconComponent,
  ],
  templateUrl: './layout.component.html',
  styleUrl: './layout.component.scss',
})
export class LayoutComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);

  readonly navLinks = [
    { path: '/dashboard', label: 'Dashboard', icon: 'dashboard' },
    { path: '/customers', label: 'Customers', icon: 'people' },
    { path: '/cases', label: 'Cases', icon: 'confirmation_number' },
  ];

  /** The currently signed-in user (or null). */
  get user() {
    return this.auth.currentUser();
  }

  /** Asks for confirmation, then logs the user out (only on confirm). */
  logout(): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        title: 'Sign out',
        message: 'Are you sure you want to sign out?',
        confirmText: 'Sign out',
        cancelText: 'Cancel',
        icon: 'logout',
      },
      width: '400px',
      maxWidth: '92vw',
      autoFocus: false,
    });
    ref.afterClosed().subscribe((confirmed) => {
      if (confirmed) {
        this.auth.logout();
        this.router.navigateByUrl('/login');
      }
    });
  }
}

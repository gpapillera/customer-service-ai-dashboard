import { Component, inject, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { CsIconComponent } from '../shared/cs-icon.component';
import { ConfirmDialogComponent } from '../shared/confirm-dialog.component';
import { CustomerAuthService } from './customer-auth.service';
import { AccountPanelComponent } from './account-panel.component';

/**
 * Lightweight customer-portal shell: a top bar (ServiceAI brand, customer
 * name, account, logout) with a <router-outlet> below. No sidebar — deliberately
 * simpler than the staff LayoutComponent, but reusing the same design tokens
 * so it belongs to the same product.
 */
@Component({
  selector: 'app-customer-layout',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterOutlet, CsIconComponent, AccountPanelComponent, MatDialogModule],
  templateUrl: './customer-layout.component.html',
  styleUrl: './customer-layout.component.scss',
})
export class CustomerLayoutComponent {
  private readonly auth = inject(CustomerAuthService);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);

  /** Reference to the account panel so we can open it. */
  private readonly accountPanel = viewChild.required(AccountPanelComponent);

  /** The signed-in customer's display name. */
  get name(): string {
    return this.auth.getName();
  }

  /** Opens the right-anchored account panel. */
  openAccount(): void {
    this.accountPanel().show();
  }

  /** Asks for confirmation, then logs the customer out (only on confirm). */
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
        this.router.navigateByUrl('/customer/login');
      }
    });
  }
}

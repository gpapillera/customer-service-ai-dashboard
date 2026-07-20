import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { CsIconComponent } from '../shared/cs-icon.component';
import { CustomerAuthService } from './customer-auth.service';

/**
 * Lightweight customer-portal shell: a top bar (ServiceAI brand, customer
 * name, logout) with a <router-outlet> below. No sidebar — deliberately
 * simpler than the staff LayoutComponent, but reusing the same design tokens
 * so it belongs to the same product.
 */
@Component({
  selector: 'app-customer-layout',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterOutlet, CsIconComponent],
  templateUrl: './customer-layout.component.html',
  styleUrl: './customer-layout.component.scss',
})
export class CustomerLayoutComponent {
  private readonly auth = inject(CustomerAuthService);
  private readonly router = inject(Router);

  /** The signed-in customer's display name. */
  get name(): string {
    return this.auth.getName();
  }

  /** Logs the customer out and returns to the login screen. */
  logout(): void {
    this.auth.logout();
    this.router.navigateByUrl('/customer/login');
  }
}

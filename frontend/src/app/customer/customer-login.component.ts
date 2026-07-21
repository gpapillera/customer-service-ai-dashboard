import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatDialog } from '@angular/material/dialog';
import { CsIconComponent } from '../shared/cs-icon.component';
import { CustomerAuthService } from './customer-auth.service';
import { SignupDialogComponent } from './signup-dialog.component';

/**
 * Customer login screen. Issues a customer JWT via CustomerAuthService and
 * redirects to /customer/cases on success. Also hosts the public self-signup
 * modal (no password collected — the customer sets one via the emailed link).
 */
@Component({
  selector: 'app-customer-login',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    RouterLink,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    CsIconComponent,
  ],
  templateUrl: './customer-login.component.html',
  styleUrl: './customer-login.component.scss',
})
export class CustomerLoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(CustomerAuthService);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  loading = false;
  error: string | null = null;
  /** When set, shows the "check your email" success panel instead of the form. */
  readonly signedUpEmail = signal<string | null>(null);

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.loading = true;
    this.error = null;
    const { email, password } = this.form.getRawValue();
    this.auth.login(email, password).subscribe({
      next: () => this.router.navigateByUrl('/customer/cases'),
      error: () => {
        this.loading = false;
        this.error = 'Invalid email or password.';
      },
    });
  }

  /** Opens the self-signup modal. On success, swaps to the email-check panel. */
  openSignup(): void {
    this.error = null;
    const ref = this.dialog.open(SignupDialogComponent, {
      width: '440px',
      maxWidth: '92vw',
      autoFocus: 'first-name',
    });
    ref.afterClosed().subscribe((email: string | undefined) => {
      if (email) {
        this.signedUpEmail.set(email);
      }
    });
  }

  /** Returns from the success panel back to the login form. */
  backToLogin(): void {
    this.signedUpEmail.set(null);
    this.form.reset();
  }
}

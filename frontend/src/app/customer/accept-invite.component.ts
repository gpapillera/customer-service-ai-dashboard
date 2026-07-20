import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { CsIconComponent } from '../shared/cs-icon.component';
import { CustomerService } from './customer.service';

/**
 * Accept-invite screen (public). Reads the ?token= query param, validates it,
 * and — if valid — shows a "set your password" form. On submit it calls the
 * accept-invite endpoint, then shows a success state linking to login. Invalid
 * / expired / used tokens show a clear message, never a stack trace.
 */
@Component({
  selector: 'app-accept-invite',
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
  templateUrl: './accept-invite.component.html',
  styleUrl: './accept-invite.component.scss',
})
export class AcceptInviteComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);
  private readonly customerService = inject(CustomerService);

  /** The invite token from the URL. */
  token: string | null = null;

  /** Loading state while validating the token. */
  readonly validating = signal(true);
  /** True once the token is confirmed valid. */
  readonly valid = signal(false);
  /** Customer display name for the "set your password" heading. */
  readonly customerName = signal<string | null>(null);
  /** Error message for invalid/expired/used tokens. */
  readonly invalidError = signal<string | null>(null);
  /** True after a successful password set. */
  readonly done = signal(false);
  /** Error shown on the password form. */
  readonly formError = signal<string | null>(null);
  readonly saving = signal(false);

  readonly form = this.fb.nonNullable.group({
    password: ['', [Validators.required, Validators.minLength(8)]],
    confirm: ['', Validators.required],
  });

  ngOnInit(): void {
    this.token = this.route.snapshot.queryParamMap.get('token');
    if (!this.token) {
      this.validating.set(false);
      this.invalidError.set('This invite link is missing its token.');
      return;
    }
    this.customerService.validateInvite(this.token).subscribe({
      next: (res) => {
        this.validating.set(false);
        if (res.valid) {
          this.valid.set(true);
          this.customerName.set(res.customerName);
        } else {
          this.invalidError.set(
            'This invite link is invalid, expired, or has already been used.',
          );
        }
      },
      error: () => {
        this.validating.set(false);
        this.invalidError.set(
          'We could not verify this invite link. Please request a new one.',
        );
      },
    });
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { password, confirm } = this.form.getRawValue();
    if (password !== confirm) {
      this.formError.set('Passwords do not match.');
      return;
    }
    this.saving.set(true);
    this.formError.set(null);
    this.customerService.acceptInvite(this.token!, password).subscribe({
      next: () => {
        this.saving.set(false);
        this.done.set(true);
      },
      error: (err) => {
        this.saving.set(false);
        const msg = err?.error?.error as string | undefined;
        this.formError.set(
          msg ?? 'We could not set your password. The link may have expired.',
        );
      },
    });
  }

  goToLogin(): void {
    this.router.navigateByUrl('/customer/login');
  }
}

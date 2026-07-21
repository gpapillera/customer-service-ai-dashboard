import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { CsIconComponent } from '../shared/cs-icon.component';
import { HttpClient } from '@angular/common/http';

/**
 * Public staff password-reset screen. Reads the ?token= query param, validates
 * it by attempting the reset, and shows a "set your password" form. On submit
 * it calls POST /api/auth/reset-password, then shows a success state linking
 * to login. Invalid/expired/used tokens show a clear message.
 *
 * Mirrors the customer AcceptInviteComponent's UI pattern for visual
 * consistency.
 */
@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    CsIconComponent,
  ],
  templateUrl: './reset-password.component.html',
  styleUrl: './reset-password.component.scss',
})
export class ResetPasswordComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);
  private readonly http = inject(HttpClient);

  /** The reset token from the URL. */
  token: string | null = null;

  /** Whether we have a token to work with. */
  readonly hasToken = signal(false);
  /** True after a successful password reset. */
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
      this.formError.set('This reset link is missing its token.');
      return;
    }
    this.hasToken.set(true);
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
    this.http.post<{ message: string }>('/api/auth/reset-password', {
      token: this.token,
      password,
    }).subscribe({
      next: () => {
        this.saving.set(false);
        this.done.set(true);
      },
      error: (err) => {
        this.saving.set(false);
        const msg = (err?.error?.error as string)
          ?? 'This reset link is invalid, expired, or has already been used.';
        this.formError.set(msg);
      },
    });
  }

  goToLogin(): void {
    this.router.navigateByUrl('/login');
  }
}

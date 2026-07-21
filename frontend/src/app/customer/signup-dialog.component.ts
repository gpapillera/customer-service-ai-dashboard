import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { CsIconComponent } from '../shared/cs-icon.component';
import { CustomerService } from './customer.service';
import { RegisterCustomer } from '../shared/models';

/**
 * Public customer self-registration modal. Collects name/email/phone/company/
 * address (never a password) and calls POST /api/customer-auth/register. On
 * success it closes the dialog and signals the parent to show the
 * "check your email" message. On the duplicate-email error it surfaces the
 * server's message inline rather than a generic failure.
 */
@Component({
  selector: 'app-signup-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatDialogModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    MatIconModule,
    CsIconComponent,
  ],
  templateUrl: './signup-dialog.component.html',
  styleUrl: './signup-dialog.component.scss',
})
export class SignupDialogComponent {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(CustomerService);
  private readonly dialogRef = inject(MatDialogRef<SignupDialogComponent>);

  readonly form = this.fb.nonNullable.group({
    fullName: ['', Validators.required],
    email: ['', [Validators.required, Validators.email]],
    phone: ['' as string],
    company: ['' as string],
    address: ['' as string],
  });

  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    const dto = this.form.getRawValue() as RegisterCustomer;
    this.service.register(dto).subscribe({
      next: () => this.dialogRef.close(dto.email),
      error: (err) => {
        this.saving.set(false);
        const msg = err?.error?.error as string | undefined;
        this.error.set(
          msg ?? 'We could not complete your sign-up. Please try again.',
        );
      },
    });
  }

  cancel(): void {
    this.dialogRef.close(false);
  }
}

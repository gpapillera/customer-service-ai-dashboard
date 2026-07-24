import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { CsIconComponent } from './cs-icon.component';
import { AuthService } from '../auth/auth.service';
import { ConfirmDialogComponent } from './confirm-dialog.component';
import { StaffProfile, UpdateStaffProfile } from './models';

/**
 * Right-anchored slide-in account panel for staff (agents + admins). Opened
 * from the staff layout top bar, it shows the signed-in staff member's profile
 * (email read-only) and lets them edit their display name, or trigger a
 * password-reset email.
 *
 * Visual design mirrors the customer `AccountPanelComponent` (Phase 8) for
 * consistency — same slide-in pattern, same CSS variables, same field layout.
 */
@Component({
  selector: 'app-staff-account-panel',
  standalone: true,
  imports: [CommonModule, FormsModule, CsIconComponent],
  templateUrl: './staff-account-panel.component.html',
  styleUrl: './staff-account-panel.component.scss',
})
export class StaffAccountPanelComponent {
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly router = inject(Router);

  /** Whether the panel is currently open. */
  readonly open = signal(false);

  /** The loaded profile (null until first load). */
  readonly profile = signal<StaffProfile | null>(null);

  /** Edit mode toggle. */
  readonly editing = signal(false);

  /** Working copy while editing. */
  readonly draft = signal<UpdateStaffProfile>({ fullName: '' });

  /** Transient status messages. */
  readonly saving = signal(false);
  readonly resetSent = signal(false);
  readonly error = signal<string | null>(null);

  /** Opens the panel and loads the profile. */
  show(): void {
    this.open.set(true);
    this.editing.set(false);
    this.resetSent.set(false);
    this.error.set(null);
    this.load();
  }

  /** Closes the panel. */
  hide(): void {
    this.open.set(false);
  }

  private load(): void {
    this.auth.getProfile().subscribe({
      next: (p) => {
        this.profile.set(p);
        this.draft.set({ fullName: p.fullName });
      },
      error: () => this.error.set('Could not load your profile.'),
    });
  }

  /** Enters edit mode with a fresh draft. */
  startEdit(): void {
    const p = this.profile();
    if (!p) return;
    this.draft.set({ fullName: p.fullName });
    this.editing.set(true);
    this.error.set(null);
  }

  /** Cancels edit mode. */
  cancelEdit(): void {
    this.editing.set(false);
    this.error.set(null);
  }

  /** Saves the edited profile. */
  save(): void {
    this.saving.set(true);
    this.error.set(null);
    this.auth.updateProfile(this.draft()).subscribe({
      next: () => {
        this.saving.set(false);
        this.editing.set(false);
        this.load();
      },
      error: (err) => {
        this.saving.set(false);
        const msg = err?.error?.error ?? err?.error?.title as string | undefined;
        this.error.set(msg ?? 'Could not save your profile.');
      },
    });
  }

  /** Shows a confirmation dialog, then signs out. */
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

  /** Requests a password reset email. */
  requestReset(): void {
    this.error.set(null);
    this.auth.requestPasswordReset().subscribe({
      next: () => this.resetSent.set(true),
      error: (err) => {
        const msg = err?.error?.error as string | undefined;
        this.error.set(msg ?? 'Could not send reset email.');
      },
    });
  }
}

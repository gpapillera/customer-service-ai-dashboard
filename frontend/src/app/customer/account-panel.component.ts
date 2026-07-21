import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CsIconComponent } from '../shared/cs-icon.component';
import { CustomerService } from './customer.service';
import { CustomerProfile, UpdateCustomerProfile } from '../shared/models';

/**
 * Right-anchored slide-in account panel. Opened from the customer top bar,
 * it shows the signed-in customer's profile (email read-only) and lets them
 * edit Name/Phone/Company/Address, or trigger a password-reset email.
 *
 * Rendered inline (position: fixed via CSS) so it floats above the router
 * outlet and can be dismissed by clicking the scrim.
 */
@Component({
  selector: 'app-account-panel',
  standalone: true,
  imports: [CommonModule, FormsModule, CsIconComponent],
  templateUrl: './account-panel.component.html',
  styleUrl: './account-panel.component.scss',
})
export class AccountPanelComponent {
  private readonly customer = inject(CustomerService);

  /** Whether the panel is currently open. */
  readonly open = signal(false);

  /** The loaded profile (null until first load). */
  readonly profile = signal<CustomerProfile | null>(null);

  /** Edit mode toggle. */
  readonly editing = signal(false);

  /** Working copy while editing. */
  readonly draft = signal<UpdateCustomerProfile>({ name: '' });

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
    this.customer.getProfile().subscribe({
      next: (p) => {
        this.profile.set(p);
        this.draft.set({
          name: p.name,
          phone: p.phone ?? null,
          company: p.company ?? null,
          address: p.address ?? null,
        });
      },
      error: () => this.error.set('Could not load your profile.'),
    });
  }

  /** Enters edit mode with a fresh draft. */
  startEdit(): void {
    const p = this.profile();
    if (!p) return;
    this.draft.set({
      name: p.name,
      phone: p.phone ?? null,
      company: p.company ?? null,
      address: p.address ?? null,
    });
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
    this.customer.updateProfile(this.draft()).subscribe({
      next: () => {
        this.saving.set(false);
        this.editing.set(false);
        this.load();
      },
      error: () => {
        this.saving.set(false);
        this.error.set('Could not save changes. Please try again.');
      },
    });
  }

  /** Requests a password-reset email (reuses the invite token flow). */
  requestReset(): void {
    this.error.set(null);
    this.customer.requestPasswordReset().subscribe({
      next: () => this.resetSent.set(true),
      error: () => this.error.set('Could not send the reset email.'),
    });
  }
}

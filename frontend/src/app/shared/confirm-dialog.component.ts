import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { CsIconComponent } from './cs-icon.component';

/** Data accepted by the generic confirmation dialog. */
export interface ConfirmDialogData {
  title: string;
  message: string;
  confirmText: string;
  cancelText?: string;
  icon?: string;
}

/**
 * Reusable confirmation dialog with the app's modal shell (header + × close,
 * footer Cancel + solid indigo confirm). Returns `true` on confirm, `false`
 * (or null) on cancel/close.
 */
@Component({
  selector: 'app-confirm-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule, MatButtonModule, MatIconModule, CsIconComponent],
  template: `
    <div class="modal-head">
      <h2>{{ data.title }}</h2>
      <button mat-icon-button type="button" (click)="close()" aria-label="Close">
        <cs-icon name="close"></cs-icon>
      </button>
    </div>
    <div class="confirm-body">
      @if (data.icon) {
        <cs-icon [name]="data.icon" class="confirm-icon"></cs-icon>
      }
      <p class="confirm-message">{{ data.message }}</p>
    </div>
    <div class="actions">
      <button mat-button type="button" class="text-btn" (click)="close()">
        {{ data.cancelText || 'Cancel' }}
      </button>
      <button mat-flat-button color="primary" type="button" (click)="confirm()">
        {{ data.confirmText }}
      </button>
    </div>
  `,
  styles: [
    `
      :host {
        display: block;
        padding: 0.25rem 0.5rem 0.5rem;
      }
      .modal-head {
        display: flex;
        align-items: center;
        justify-content: space-between;
        margin-bottom: 0.25rem;
      }
      .modal-head h2 {
        margin: 0;
        font-size: 1.2rem;
        font-weight: 700;
        letter-spacing: -0.01em;
      }
      .confirm-body {
        display: flex;
        align-items: flex-start;
        gap: 0.75rem;
        padding: 0.25rem 0.25rem 0.5rem;
      }
      .confirm-icon {
        color: var(--cs-accent);
        flex-shrink: 0;
      }
      .confirm-message {
        margin: 0;
        color: var(--cs-text-muted);
        line-height: 1.5;
      }
      .actions {
        display: flex;
        justify-content: flex-end;
        gap: 0.75rem;
        margin-top: 0.5rem;
      }
      .text-btn {
        color: var(--cs-text-muted);
      }
    `,
  ],
})
export class ConfirmDialogComponent {
  private readonly dialogRef = inject(MatDialogRef<ConfirmDialogComponent>);
  readonly data = inject<ConfirmDialogData>(MAT_DIALOG_DATA);

  /** Confirms the action. */
  confirm(): void {
    this.dialogRef.close(true);
  }

  /** Cancels the action. */
  close(): void {
    this.dialogRef.close(false);
  }
}

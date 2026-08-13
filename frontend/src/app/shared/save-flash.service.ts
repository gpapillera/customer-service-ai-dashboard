import { Injectable, signal } from '@angular/core';

/**
 * Global, read-only "change saved / in effect" indicator.
 *
 * Any component or panel can call `show(...)` to flash a calm, auto-fading
 * badge at the TOP of the viewport — not buried inside a control. This gives
 * the user instant confirmation that an edit they made actually took effect
 * server-side, without a blocking dialog.
 *
 * One instance (providedIn:'root') backs a single banner component mounted in
 * AppComponent, so the flash works on every route and any viewport width.
 */
@Injectable({ providedIn: 'root' })
export class SaveFlashService {
  /** The current message, or null when nothing is showing. */
  readonly message = signal<string | null>(null);

  private timer: ReturnType<typeof setTimeout> | null = null;

  /**
   * Shows `msg` for `ms` milliseconds (default 2200), then clears it.
   * Calling show() again resets the timer so rapid changes don't flicker.
   */
  show(msg: string, ms = 2200): void {
    this.message.set(msg);
    if (this.timer) clearTimeout(this.timer);
    this.timer = setTimeout(() => this.message.set(null), ms);
  }

  /** Immediately clears any visible flash. */
  clear(): void {
    if (this.timer) clearTimeout(this.timer);
    this.timer = null;
    this.message.set(null);
  }
}

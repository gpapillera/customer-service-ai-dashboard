import { Injectable, inject, signal, effect } from '@angular/core';
import { AuthService } from '../auth/auth.service';

/**
 * Manages the application colour theme (light / dark).
 *
 * - Persists the user's choice to ``localStorage``, scoped to the currently
 *   logged-in user (key: ``cs-theme-{userName}``) so that different accounts
 *   (admin, agent, etc.) can have independent theme settings.
 * - Defaults to the OS preference (``prefers-color-scheme``).
 * - Applies a ``data-theme="dark"`` attribute on ``<html>`` so all CSS
 *   variables in ``styles.scss`` react immediately.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  /** ``true`` when dark mode is active. */
  readonly isDark = signal(false);

  private readonly auth = inject(AuthService);

  constructor() {
    // 1. Load the theme preference for the current user scope.
    this.loadTheme();

    // 2. React to user changes (login/logout/switch) by reloading preference.
    effect(() => {
      this.auth.currentUser(); // subscribe to user changes
      this.loadTheme();
    }, { allowSignalWrites: true });

    // 3. Apply the theme attribute and persist whenever the signal changes.
    effect(() => {
      const dark = this.isDark();
      document.documentElement.setAttribute('data-theme', dark ? 'dark' : 'light');
      const key = this.storageKey();
      localStorage.setItem(key, dark ? 'dark' : 'light');
    });

    // 4. Listen for OS-level changes (only when no explicit choice stored).
    if (typeof window !== 'undefined') {
      const mq = window.matchMedia('(prefers-color-scheme: dark)');
      mq.addEventListener('change', (e) => {
        const key = this.storageKey();
        if (!localStorage.getItem(key)) {
          this.isDark.set(e.matches);
        }
      });
    }
  }

  /** Build a user-scoped localStorage key. When no user is logged in, falls
   *  back to the legacy unscoped key ``cs-theme``. */
  private storageKey(): string {
    const user = this.auth.currentUser();
    return user?.userName ? `cs-theme-${user.userName}` : 'cs-theme';
  }

  /** Read the theme preference from localStorage (scoped to current user).
   *  Falls back to OS ``prefers-color-scheme`` when no stored preference
   *  exists. Migrates from the legacy unscoped ``cs-theme`` key on first
   *  access for a user. */
  private loadTheme(): void {
    const key = this.storageKey();
    let stored = localStorage.getItem(key);

    // Migrate from the legacy unscoped key (if a user is logged in).
    if (stored === null && this.auth.currentUser()) {
      const old = localStorage.getItem('cs-theme');
      if (old === 'dark' || old === 'light') {
        stored = old;
        localStorage.setItem(key, old);
        localStorage.removeItem('cs-theme');
      }
    }

    if (stored === 'dark' || stored === 'light') {
      this.isDark.set(stored === 'dark');
    } else {
      // Fall back to OS preference.
      const prefersDark = typeof window !== 'undefined' &&
        window.matchMedia('(prefers-color-scheme: dark)').matches;
      this.isDark.set(prefersDark);
    }
  }

  /** Toggle between light and dark themes. */
  toggle(): void {
    this.isDark.update((v) => !v);
  }

  /** Convenience: set a specific theme. */
  setTheme(dark: boolean): void {
    this.isDark.set(dark);
  }
}

import { Injectable, signal, effect } from '@angular/core';

/**
 * Manages the application colour theme (light / dark).
 *
 * - Persists the user's choice to ``localStorage`` (key: ``cs-theme``).
 * - Defaults to the OS preference (``prefers-color-scheme``).
 * - Applies a ``data-theme="dark"`` attribute on ``<html>`` so all CSS
 *   variables in ``styles.scss`` react immediately.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  /** ``true`` when dark mode is active. */
  readonly isDark = signal(false);

  constructor() {
    // 1. Check localStorage first (user override).
    const stored = localStorage.getItem('cs-theme');
    if (stored === 'dark' || stored === 'light') {
      this.isDark.set(stored === 'dark');
    } else {
      // 2. Fall back to OS preference.
      const prefersDark = typeof window !== 'undefined' &&
        window.matchMedia('(prefers-color-scheme: dark)').matches;
      this.isDark.set(prefersDark);
    }

    // 3. Apply the theme attribute and persist whenever the signal changes.
    effect(() => {
      const dark = this.isDark();
      document.documentElement.setAttribute('data-theme', dark ? 'dark' : 'light');
      localStorage.setItem('cs-theme', dark ? 'dark' : 'light');
    });

    // 4. Listen for OS-level changes (only when no explicit user choice stored).
    if (typeof window !== 'undefined') {
      const mq = window.matchMedia('(prefers-color-scheme: dark)');
      mq.addEventListener('change', (e) => {
        const stored = localStorage.getItem('cs-theme');
        if (!stored) {
          this.isDark.set(e.matches);
        }
      });
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

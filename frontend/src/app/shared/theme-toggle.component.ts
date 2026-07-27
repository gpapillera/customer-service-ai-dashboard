import { Component, inject, signal } from '@angular/core';
import { CsIconComponent } from './cs-icon.component';
import { ThemeService } from './theme.service';

/**
 * Animated dark/light mode toggle button for the login page.
 *
 * - Sun icon in light mode, Moon icon in dark mode.
 * - Smooth spin-scale animation on every toggle.
 * - Positioned top-right via its parent's ``.theme-toggle-corner``.
 */
@Component({
  selector: 'app-theme-toggle',
  standalone: true,
  imports: [CsIconComponent],
  template: `
    <button
      type="button"
      class="theme-toggle"
      [class.animate]="animating()"
      (click)="toggle()"
      [attr.aria-label]="theme.isDark() ? 'Switch to light mode' : 'Switch to dark mode'"
    >
      <cs-icon
        [name]="theme.isDark() ? 'moon' : 'sun'"
        [size]="22"
        class="toggle-icon"
      ></cs-icon>
    </button>
  `,
  styles: [
    `
      .theme-toggle {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 44px;
        height: 44px;
        border-radius: 12px;
        border: 1px solid var(--cs-border);
        background: var(--cs-surface);
        color: var(--cs-text);
        cursor: pointer;
        transition: background 0.25s var(--cs-ease),
                    border-color 0.25s var(--cs-ease),
                    box-shadow 0.25s var(--cs-ease);
        box-shadow: 0 1px 3px rgba(0, 0, 0, 0.06);
        outline: none;
        -webkit-tap-highlight-color: transparent;
      }
      .theme-toggle:hover {
        background: var(--cs-bg-subtle);
        border-color: var(--cs-accent);
        box-shadow: 0 2px 8px rgba(79, 70, 229, 0.15);
      }
      .theme-toggle:focus-visible {
        outline: 2px solid var(--cs-accent);
        outline-offset: 2px;
      }

      .toggle-icon {
        display: flex;
        align-items: center;
        justify-content: center;
        transition: transform 0.35s var(--cs-ease);
      }

      .theme-toggle.animate .toggle-icon {
        animation: spin-toggle 0.45s cubic-bezier(0.34, 1.56, 0.64, 1);
      }

      @keyframes spin-toggle {
        0%   { transform: rotate(0deg)   scale(1); }
        50%  { transform: rotate(180deg)  scale(0.75); }
        100% { transform: rotate(360deg)  scale(1); }
      }

      @media (prefers-reduced-motion: reduce) {
        .theme-toggle.animate .toggle-icon {
          animation: none;
        }
        .toggle-icon {
          transition: none;
        }
      }
    `,
  ],
})
export class ThemeToggleComponent {
  readonly theme = inject(ThemeService);
  private readonly anim = signal(false);
  /** Expose readonly for template binding. */
  readonly animating = this.anim.asReadonly();
  private timer: ReturnType<typeof setTimeout> | undefined;

  /** Toggle theme and trigger the spin animation. */
  toggle(): void {
    this.theme.toggle();
    this.anim.set(true);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.anim.set(false), 500);
  }
}

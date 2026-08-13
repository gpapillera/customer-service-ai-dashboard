import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CsIconComponent } from './cs-icon.component';
import { SaveFlashService } from './save-flash.service';

/**
 * Global, top-of-viewport "change saved" flash. Mounted once in AppComponent
 * so every page/panel can trigger it via SaveFlashService.show(...). Fixed to
 * the top center, full-width on small screens, and fades in/out via CSS.
 */
@Component({
  selector: 'app-save-flash',
  standalone: true,
  imports: [CommonModule, CsIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (flash.message(); as msg) {
      <div class="save-flash" role="status" aria-live="polite">
        <cs-icon name="check_circle"></cs-icon>
        <span>{{ msg }}</span>
      </div>
    }
  `,
  styles: [
    `
      :host {
        position: fixed;
        top: 14px;
        left: 50%;
        transform: translateX(-50%);
        z-index: 1000;
        pointer-events: none;
        width: max-content;
        max-width: calc(100vw - 24px);
      }
      .save-flash {
        display: inline-flex;
        align-items: center;
        gap: 8px;
        padding: 9px 14px;
        border-radius: var(--cs-radius, 14px);
        background: var(--cs-success-bg, rgba(16, 185, 129, 0.14));
        color: var(--cs-success, #10b981);
        font-size: 0.85rem;
        font-weight: 600;
        box-shadow: 0 6px 20px rgba(15, 23, 42, 0.18);
        animation: save-flash-in 0.18s var(--cs-ease, ease) both;
        white-space: nowrap;
      }
      .save-flash cs-icon {
        font-size: 1.1rem;
        width: 1.1rem;
        height: 1.1rem;
      }
      @keyframes save-flash-in {
        from {
          opacity: 0;
          transform: translateY(-6px);
        }
        to {
          opacity: 1;
          transform: translateY(0);
        }
      }
      @media (max-width: 600px) {
        :host {
          left: 12px;
          right: 12px;
          transform: none;
          width: auto;
        }
        .save-flash {
          width: 100%;
          justify-content: center;
          white-space: normal;
        }
      }
    `,
  ],
})
export class SaveFlashComponent {
  readonly flash = inject(SaveFlashService);
}

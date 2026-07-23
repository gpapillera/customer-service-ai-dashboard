import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TooltipItem } from './tooltip-data';

/**
 * A minimal floating card rendered inside a CDK overlay by
 * <code>CsTooltipDirective</code>. Apple-like design: subtle shadow,
 * rounded corners, small type.
 */
@Component({
  selector: 'cs-tooltip',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="cs-tt">
      @for (item of items; track item.label) {
        <div class="cs-tt__row">
          <span class="cs-tt__label">{{ item.label }}</span>
          <span class="cs-tt__value">{{ item.value }}</span>
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }
    .cs-tt {
      background: var(--cs-bg-raised, #fff);
      border: 1px solid var(--cs-border, #e2e8f0);
      border-radius: 10px;
      padding: 0.5rem 0.75rem;
      box-shadow: 0 4px 16px rgba(0,0,0,0.10), 0 1px 4px rgba(0,0,0,0.06);
      font-size: 0.75rem;
      line-height: 1.5;
      max-width: 260px;
      pointer-events: none;
    }
    .cs-tt__row {
      display: flex;
      justify-content: space-between;
      gap: 1rem;
      white-space: nowrap;
    }
    .cs-tt__label {
      color: var(--cs-text-muted, #64748b);
      font-weight: 500;
    }
    .cs-tt__value {
      color: var(--cs-text, #1e293b);
      font-weight: 600;
      text-align: right;
      overflow: hidden;
      text-overflow: ellipsis;
      max-width: 140px;
    }
    [data-theme="dark"] .cs-tt {
      background: var(--cs-bg-raised, #1e293b);
      border-color: var(--cs-border, #334155);
      box-shadow: 0 4px 16px rgba(0,0,0,0.35);
    }
    [data-theme="dark"] .cs-tt__label {
      color: var(--cs-text-muted, #94a3b8);
    }
    [data-theme="dark"] .cs-tt__value {
      color: var(--cs-text, #f1f5f9);
    }
  `],
})
export class TooltipComponent {
  @Input() items: TooltipItem[] = [];
}

import {
  Directive,
  Input,
  ElementRef,
  OnDestroy,
  inject,
} from '@angular/core';
import { Overlay, OverlayRef } from '@angular/cdk/overlay';
import { ComponentPortal } from '@angular/cdk/portal';
import { TooltipComponent } from './tooltip.component';
import { TooltipData } from './tooltip-data';

/**
 * Lightweight rich tooltip directive using Angular CDK Overlay.
 *
 * Usage:
 * ```html
 * <span [csTooltip]="{ items: [{ label: 'Status', value: 'Active' }] }">…</span>
 * ```
 *
 * The tooltip appears above the host element on hover/focus and is removed
 * when the pointer leaves or the element loses focus.
 */
@Directive({
  selector: '[csTooltip]',
  standalone: true,
  host: {
    '(mouseenter)': 'show()',
    '(mouseleave)': 'hide()',
    '(focusin)': 'show()',
    '(focusout)': 'hide()',
  },
})
export class CsTooltipDirective implements OnDestroy {
  @Input({ required: true, alias: 'csTooltip' }) data!: TooltipData;

  private readonly overlay = inject(Overlay);
  private readonly elementRef = inject<ElementRef<HTMLElement>>(ElementRef);
  private overlayRef: OverlayRef | null = null;

  /** Delay before showing (ms) — prevents flicker when passing over pills. */
  private showTimeout: ReturnType<typeof setTimeout> | undefined;
  /** Delay before hiding (ms). */
  private hideTimeout: ReturnType<typeof setTimeout> | undefined;

  show(): void {
    if (this.overlayRef) return;
    clearTimeout(this.hideTimeout);

    this.showTimeout = setTimeout(() => {
      this.showTimeout = undefined;

      const positionStrategy = this.overlay
        .position()
        .flexibleConnectedTo(this.elementRef)
        .withPositions([
          { originX: 'center', originY: 'top', overlayX: 'center', overlayY: 'bottom', offsetY: -6 },
          { originX: 'center', originY: 'bottom', overlayX: 'center', overlayY: 'top', offsetY: 6 },
        ])
        .withPush(true);

      this.overlayRef = this.overlay.create({
        positionStrategy,
        scrollStrategy: this.overlay.scrollStrategies.reposition(),
        disposeOnNavigation: true,
      });

      const portal = new ComponentPortal(TooltipComponent);
      const ref = this.overlayRef.attach(portal);
      ref.instance.items = this.data.items;
    }, 300);
  }

  hide(): void {
    clearTimeout(this.showTimeout);
    this.showTimeout = undefined;

    this.hideTimeout = setTimeout(() => {
      this.hideTimeout = undefined;
      this.destroyOverlay();
    }, 100);
  }

  private destroyOverlay(): void {
    if (this.overlayRef) {
      this.overlayRef.detach();
      this.overlayRef.dispose();
      this.overlayRef = null;
    }
  }

  ngOnDestroy(): void {
    clearTimeout(this.showTimeout);
    clearTimeout(this.hideTimeout);
    this.destroyOverlay();
  }
}

import {
  Directive,
  ElementRef,
  OnInit,
  OnDestroy,
  Input,
  inject,
} from '@angular/core';

/**
 * Adds a subtle "scroll into view" reveal animation (fade + rise) to any
 * element. Uses IntersectionObserver so it only animates once when the
 * element enters the viewport — giving the UI a calm, alive feeling without
 * being flashy.
 *
 * Usage: <div appReveal class="reveal"> ... </div>
 */
@Directive({
  selector: '[appReveal]',
  standalone: true,
})
export class RevealDirective implements OnInit, OnDestroy {
  private readonly host = inject(ElementRef<HTMLElement>);
  private observer?: IntersectionObserver;

  /** Optional delay (ms) before the reveal transition starts. */
  @Input() revealDelay = 0;

  ngOnInit(): void {
    const el = this.host.nativeElement;
    el.classList.add('reveal');
    if (this.revealDelay) {
      el.style.transitionDelay = `${this.revealDelay}ms`;
    }

    if (typeof IntersectionObserver === 'undefined') {
      el.classList.add('is-visible');
      return;
    }

    this.observer = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (entry.isIntersecting) {
            el.classList.add('is-visible');
            this.observer?.unobserve(el);
          }
        }
      },
      { threshold: 0.12 },
    );
    this.observer.observe(el);
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
  }
}

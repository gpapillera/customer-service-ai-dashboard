import { DestroyRef, Injectable, inject } from '@angular/core';
import { Router, NavigationStart, NavigationEnd, NavigationCancel, NavigationError } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { signal } from '@angular/core';

/**
 * Shared route-change loading state. Pages subscribe to `loading` to show
 * their own in-page spinner (below the header / search / filters) on every
 * navigation, not just the first load. A minimum display time keeps the
 * spinner perceptible even for fast (eager) route changes.
 */
@Injectable({ providedIn: 'root' })
export class RouteLoadingService {
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  /** True while a route navigation is in progress. */
  readonly loading = signal(false);

  private navStart = 0;
  private hideTimer: ReturnType<typeof setTimeout> | null = null;
  /** Keep the spinner visible at least this long so fast navigations are perceptible. */
  private readonly minDisplayMs = 350;

  constructor() {
    this.router.events.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((event) => {
      if (event instanceof NavigationStart) {
        if (this.hideTimer) {
          clearTimeout(this.hideTimer);
          this.hideTimer = null;
        }
        this.navStart = Date.now();
        this.loading.set(true);
      } else if (
        event instanceof NavigationEnd ||
        event instanceof NavigationCancel ||
        event instanceof NavigationError
      ) {
        const elapsed = Date.now() - this.navStart;
        const wait = Math.max(0, this.minDisplayMs - elapsed);
        this.hideTimer = setTimeout(() => {
          this.loading.set(false);
          this.hideTimer = null;
        }, wait);
      }
    });
  }
}

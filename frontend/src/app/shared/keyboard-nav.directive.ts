import { Directive, ElementRef, HostListener, inject, Input } from '@angular/core';

/**
 * A directive that enables arrow-key (Up/Down) navigation between focusable
 * children inside its host container. Also supports Home (first), End (last),
 * and Enter activation.
 *
 * Usage:
 *   <ul appKbdNav>
 *     <li tabindex="-1" (click)="open(item)">...</li>
 *     <li tabindex="-1" (click)="open(item)">...</li>
 *   </ul>
 *
 * The first focusable child gets tabindex="0" automatically; others get "-1".
 * Roving tabindex keeps only one item in the Tab order at any time.
 */
@Directive({
  selector: '[appKbdNav]',
  standalone: true,
})
export class KbdNavDirective {
  private readonly el = inject(ElementRef<HTMLElement>);

  /** CSS selector for focusable children. */
  @Input() kbdNavItem = 'button, a, [tabindex], li, tr';

  /** When true, navigation wraps from last to first and vice versa. */
  @Input() kbdNavWrap = false;

  private get items(): HTMLElement[] {
    const raw = this.el.nativeElement.querySelectorAll(this.kbdNavItem);
    return Array.from(raw).filter((el): el is HTMLElement => {
      if (!(el instanceof HTMLElement)) return false;
      if (el.hasAttribute('disabled')) return false;
      const tab = el.getAttribute('tabindex');
      if (tab === '-1') return false;
      return true;
    });
  }

  /** Set tabindex so only the active item is reachable via Tab. */
  private setActiveIndex(index: number): void {
    const items = this.items;
    items.forEach((item, i) => {
      item.tabIndex = i === index ? 0 : -1;
    });
  }

  /** Index of the currently-focused child item. */
  private get focusedIndex(): number {
    const focused = this.el.nativeElement.ownerDocument?.activeElement;
    if (!focused || !this.el.nativeElement.contains(focused)) return -1;
    return this.items.indexOf(focused as HTMLElement);
  }

  private focusItem(index: number): void {
    const items = this.items;
    if (index < 0 || index >= items.length) return;
    items[index].focus();
    this.setActiveIndex(index);
  }

  private moveFocus(delta: number): void {
    const items = this.items;
    if (items.length === 0) return;
    let current = this.focusedIndex;
    if (current === -1) current = delta > 0 ? -1 : items.length;
    let next = current + delta;
    if (this.kbdNavWrap) {
      next = ((next % items.length) + items.length) % items.length;
    } else {
      next = Math.max(0, Math.min(items.length - 1, next));
    }
    this.focusItem(next);
  }

  @HostListener('keydown', ['$event'])
  onKeyDown(event: KeyboardEvent): void {
    const items = this.items;
    if (items.length === 0) return;

    if (event.key === 'ArrowDown') {
      event.preventDefault();
      this.moveFocus(1);
      return;
    }
    if (event.key === 'ArrowUp') {
      event.preventDefault();
      this.moveFocus(-1);
      return;
    }
    if (event.key === 'Home') {
      event.preventDefault();
      this.focusItem(0);
      return;
    }
    if (event.key === 'End') {
      event.preventDefault();
      this.focusItem(items.length - 1);
      return;
    }
  }

  @HostListener('focusin', ['$event'])
  onFocusIn(): void {
    const idx = this.focusedIndex;
    if (idx >= 0) this.setActiveIndex(idx);
  }

  @HostListener('focusout', ['$event'])
  onFocusOut(event: FocusEvent): void {
    const related = event.relatedTarget as HTMLElement | null;
    if (related && this.el.nativeElement.contains(related)) return;
    // Keep the first item reachable so Tab can re-enter the group.
    const items = this.items;
    if (items.length > 0) items[0].tabIndex = 0;
  }
}

/**
 * Shared date-filter helpers used by the table column header filters on the
 * Cases ("Created") and Email Log ("Date") pages.
 *
 * The preset set + filtering semantics mirror the Conversations / Messages
 * pages' date filter (see `filteredConversations` in
 * `admin-conversations.component.ts` / `conversations-list.component.ts`):
 * relative presets (today / last 7 / last 30 days), a custom from–to range,
 * and single-date presets (before / after / on-or-before / on-or-after).
 */

export type DatePreset =
  | 'all'
  | 'today'
  | '7days'
  | '30days'
  | 'custom'
  | 'beforeCustomDate'
  | 'afterCustomDate'
  | 'onOrBeforeCustomDate'
  | 'onOrAfterCustomDate';

/** All presets in display order. */
export const DATE_PRESETS: readonly DatePreset[] = [
  'all',
  'today',
  '7days',
  '30days',
  'custom',
  'beforeCustomDate',
  'afterCustomDate',
  'onOrBeforeCustomDate',
  'onOrAfterCustomDate',
];

/** Human-readable label for each preset. */
export const DATE_PRESET_LABELS: Record<DatePreset, string> = {
  all: 'All time',
  today: 'Today',
  '7days': 'Last 7 days',
  '30days': 'Last 30 days',
  custom: 'Custom range',
  beforeCustomDate: 'Before date…',
  afterCustomDate: 'After date…',
  onOrBeforeCustomDate: 'On or before…',
  onOrAfterCustomDate: 'On or after…',
};

/** True when the preset needs date input fields (custom or single-date). */
export function datePresetNeedsInput(preset: DatePreset): boolean {
  return (
    preset === 'custom' ||
    preset === 'beforeCustomDate' ||
    preset === 'afterCustomDate' ||
    preset === 'onOrBeforeCustomDate' ||
    preset === 'onOrAfterCustomDate'
  );
}

/** Labels a preset key, falling back to the raw key for unknown values. */
export function formatDatePreset(preset: DatePreset | string): string {
  return DATE_PRESET_LABELS[preset as DatePreset] ?? preset;
}

/**
 * Filters items by a date preset, comparing each item's date (as returned by
 * `dateOf`) against the preset. Pass `''` for unused date inputs.
 *
 * Semantics (identical to the Conversations pages):
 * - `today`     — on or after local midnight today
 * - `7days`     — within the last 7×24h
 * - `30days`    — within the last 30×24h
 * - `custom`    — on/after `from` (if set) AND on/before end-of-day `to` (if set)
 * - `before…`   — strictly before `single`
 * - `after…`    — on/after `single`
 * - `onOrBefore`— on or before end-of-day `single`
 * - `onOrAfter` — on or after `single`
 */
export function filterByDatePreset<T>(
  items: readonly T[],
  preset: DatePreset,
  dateOf: (item: T) => string | null | undefined,
  from: string,
  to: string,
  single: string,
): T[] {
  if (preset === 'all') return [...items];

  const now = new Date();
  const dayMs = 86_400_000;
  const value = (item: T): number => new Date(dateOf(item) ?? '').getTime();

  let list = [...items];

  if (preset === 'today') {
    const todayStart = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime();
    list = list.filter((item) => value(item) >= todayStart);
  } else if (preset === '7days') {
    const cutoff = now.getTime() - 7 * dayMs;
    list = list.filter((item) => value(item) >= cutoff);
  } else if (preset === '30days') {
    const cutoff = now.getTime() - 30 * dayMs;
    list = list.filter((item) => value(item) >= cutoff);
  } else if (preset === 'custom') {
    if (from) {
      const fromMs = new Date(from).getTime();
      if (!isNaN(fromMs)) {
        list = list.filter((item) => value(item) >= fromMs);
      }
    }
    if (to) {
      const toMs = new Date(to).getTime();
      if (!isNaN(toMs)) {
        list = list.filter((item) => value(item) <= toMs + dayMs);
      }
    }
  } else if (preset === 'beforeCustomDate') {
    if (single) {
      const singleMs = new Date(single).getTime();
      if (!isNaN(singleMs)) {
        list = list.filter((item) => value(item) < singleMs);
      }
    }
  } else if (preset === 'afterCustomDate') {
    if (single) {
      const singleMs = new Date(single).getTime();
      if (!isNaN(singleMs)) {
        list = list.filter((item) => value(item) >= singleMs);
      }
    }
  } else if (preset === 'onOrBeforeCustomDate') {
    if (single) {
      const singleMs = new Date(single).getTime();
      if (!isNaN(singleMs)) {
        list = list.filter((item) => value(item) <= singleMs + dayMs);
      }
    }
  } else if (preset === 'onOrAfterCustomDate') {
    if (single) {
      const singleMs = new Date(single).getTime();
      if (!isNaN(singleMs)) {
        list = list.filter((item) => value(item) >= singleMs);
      }
    }
  }

  return list;
}

/**
 * Vertical gap (px) kept between the trigger button and the positioned
 * dropdown, and the minimum height we ever allow the dropdown to collapse to.
 */
export const DATE_DROPDOWN_GAP = 4;

/** Placement decision for the header date-filter dropdown. */
export interface DateDropdownPlacement {
  /** True when the dropdown should open upward (above the trigger). */
  openUp: boolean;
  /** Max height in px so the dropdown fits inside the visible area. */
  maxHeight: number;
}

/**
 * Computes how to place the date-filter dropdown relative to its trigger so
 * that it stays fully visible inside the scroll container's viewport.
 *
 * The dropdown is normally `position: absolute` inside the table header, but
 * the table wrapper clips it (`overflow-x: auto` forces `overflow-y: auto`)
 * as soon as a preset narrows the result set to a few rows. We therefore
 * measure the space below vs above the trigger and pick the larger side,
 * capping the dropdown's height to the available space (it scrolls
 * internally if needed).
 */
export function computeDateDropdownPlacement(
  trigger: HTMLElement,
  scrollRoot: HTMLElement,
  naturalHeight: number,
): DateDropdownPlacement {
  const t = trigger.getBoundingClientRect();
  const s = scrollRoot.getBoundingClientRect();
  const spaceBelow = Math.max(0, s.bottom - t.bottom);
  const spaceAbove = Math.max(0, t.top - s.top);
  const openUp = spaceBelow < naturalHeight && spaceAbove > spaceBelow;
  const maxSpace = (openUp ? spaceAbove : spaceBelow) - DATE_DROPDOWN_GAP;
  return {
    openUp,
    maxHeight: Math.max(DATE_DROPDOWN_GAP, Math.min(naturalHeight, maxSpace)),
  };
}

/**
 * Positions an open `.header-filter-dropdown` with `position: fixed` anchored
 * to the viewport position of its trigger (the funnel button in the same
 * `.th-content`), clamped to the visible area and flipped upward when there
 * is more room above than below. This escapes the `.table-wrap` overflow
 * clip box entirely, so the popup (including the inline date input) stays
 * reachable even when the result table shrinks to one row.
 *
 * Works for every filter column (status, priority, category, date) — the
 * dropdown only needs to live inside a `.th-content` that also holds a
 * `.header-filter-btn`.
 *
 * Call it after the dropdown renders and again on scroll/resize while open —
 * because the element is fixed, it would otherwise detach from the trigger.
 */
export function positionHeaderDropdown(dropdown: HTMLElement, scrollRoot: HTMLElement): void {
  const th = dropdown.closest('.th-content') as HTMLElement | null;
  const trigger = (th?.querySelector('.header-filter-btn') as HTMLElement | null) ?? th;
  if (!trigger) return;

  const { openUp, maxHeight } = computeDateDropdownPlacement(
    trigger,
    scrollRoot,
    dropdown.scrollHeight,
  );
  const t = trigger.getBoundingClientRect();
  const ddWidth = dropdown.offsetWidth;
  // Left-anchor by default. Right-anchor only when left-anchoring would push
  // the popup past the right edge of the viewport (e.g. the last column on a
  // narrow screen). Never trust the computed `right`: for an absolutely-
  // positioned shrink-to-fit element the browser derives a concrete right
  // value even when `left` is the authored anchor.
  const left = t.left + ddWidth <= window.innerWidth ? t.left : t.right - ddWidth;
  // Clamp so the popup stays fully visible even when the trigger itself is
  // scrolled off-screen (horizontally scrollable table).
  const clampedLeft = Math.max(8, Math.min(left, window.innerWidth - ddWidth - 8));

  dropdown.style.position = 'fixed';
  dropdown.style.left = `${Math.round(clampedLeft)}px`;
  dropdown.style.right = 'auto';
  dropdown.style.top = openUp ? 'auto' : `${Math.round(t.bottom + DATE_DROPDOWN_GAP)}px`;
  dropdown.style.bottom = openUp ? `${Math.round(window.innerHeight - t.top + DATE_DROPDOWN_GAP)}px` : 'auto';
  dropdown.style.maxHeight = `${Math.max(64, Math.round(maxHeight))}px`;
  dropdown.classList.toggle('open-up', openUp);
  // When the popup is height-clamped and holds a date input, reveal the input:
  // the preset buttons above it would otherwise push it below the fold.
  const dateInput = dropdown.querySelector('input[type="date"]');
  if (dateInput && dropdown.scrollHeight > dropdown.clientHeight && dropdown.scrollTop === 0) {
    dropdown.scrollTop = dropdown.scrollHeight;
  }
}

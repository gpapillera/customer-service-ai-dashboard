import {
  DatePreset,
  DATE_PRESETS,
  DATE_PRESET_LABELS,
  DATE_DROPDOWN_GAP,
  datePresetNeedsInput,
  filterByDatePreset,
  formatDatePreset,
  computeDateDropdownPlacement,
} from './date-filter';

/**
 * Unit tests for the shared date-filter helper used by the Cases and Email
 * Log table header date filters. Verifies each preset's semantics against
 * the Conversations/Messages pages' date filter behavior.
 *
 * Fixtures use noon-UTC timestamps and boundaries 2+ days away from any
 * local-midnight edge, so the assertions hold in every timezone (UTC-12
 * through UTC+14).
 */
describe('date-filter', () => {
  /** Items with a `ts` field (ISO UTC) used as the filterable date. */
  interface Item {
    id: number;
    ts: string;
  }
  const dateOf = (item: Item): string => item.ts;

  /**
   * Absolute-date fixture for the custom / before / after presets.
   * - id 1: 2026-07-20T12:00Z — local date is 07-19..07-21 in any timezone
   * - id 2: 2026-07-25T12:00Z — local date is 07-24..07-26 in any timezone
   * - id 3: 2026-06-01T12:00Z — always "the past"
   * - id 4: right now
   */
  const absoluteItems = (): Item[] => [
    { id: 1, ts: '2026-07-20T12:00:00Z' },
    { id: 2, ts: '2026-07-25T12:00:00Z' },
    { id: 3, ts: '2026-06-01T12:00:00Z' },
    { id: 4, ts: new Date().toISOString() },
  ];

  /** Relative fixture for today / 7days / 30days (computed from "now"). */
  const relativeItems = (): Item[] => {
    const now = Date.now();
    return [
      { id: 1, ts: new Date(now - 1_000).toISOString() }, // seconds ago
      { id: 2, ts: new Date(now - 3 * 86_400_000).toISOString() }, // ~3 days
      { id: 3, ts: new Date(now - 20 * 86_400_000).toISOString() }, // ~20 days
      { id: 4, ts: new Date(now - 45 * 86_400_000).toISOString() }, // ~45 days
    ];
  };

  it('exposes all presets with labels', () => {
    expect(DATE_PRESETS.length).toBe(9);
    expect(DATE_PRESET_LABELS.all).toBe('All time');
    expect(formatDatePreset('7days')).toBe('Last 7 days');
    expect(formatDatePreset('custom')).toBe('Custom range');
    expect(formatDatePreset('onOrBeforeCustomDate')).toBe('On or before…');
    // Unknown keys fall back to the raw key.
    expect(formatDatePreset('bogus' as DatePreset)).toBe('bogus');
  });

  it('knows which presets need date inputs', () => {
    expect(datePresetNeedsInput('all')).toBe(false);
    expect(datePresetNeedsInput('today')).toBe(false);
    expect(datePresetNeedsInput('7days')).toBe(false);
    expect(datePresetNeedsInput('30days')).toBe(false);
    expect(datePresetNeedsInput('custom')).toBe(true);
    expect(datePresetNeedsInput('beforeCustomDate')).toBe(true);
    expect(datePresetNeedsInput('afterCustomDate')).toBe(true);
    expect(datePresetNeedsInput('onOrBeforeCustomDate')).toBe(true);
    expect(datePresetNeedsInput('onOrAfterCustomDate')).toBe(true);
  });

  it('"all" returns every item unchanged', () => {
    const list = absoluteItems();
    const result = filterByDatePreset(list, 'all', dateOf, '', '', '');
    expect(result.length).toBe(list.length);
  });

  it('"today" keeps items on or after local midnight', () => {
    const result = filterByDatePreset(relativeItems(), 'today', dateOf, '', '', '');
    expect(result.map((i) => i.id)).toEqual([1]);
  });

  it('"7days" keeps items within the last 7 days', () => {
    const result = filterByDatePreset(relativeItems(), '7days', dateOf, '', '', '');
    expect(result.map((i) => i.id)).toEqual([1, 2]);
  });

  it('"30days" keeps items within the last 30 days', () => {
    const result = filterByDatePreset(relativeItems(), '30days', dateOf, '', '', '');
    expect(result.map((i) => i.id)).toEqual([1, 2, 3]);
  });

  it('"custom" filters between from and to (inclusive of the to day)', () => {
    // id 1 lives on 07-19..07-21 locally; id 2 on 07-24..07-26 locally.
    const result = filterByDatePreset(absoluteItems(), 'custom', dateOf, '2026-07-19', '2026-07-21', '');
    expect(result.map((i) => i.id)).toEqual([1]);
  });

  it('"custom" with only "from" keeps everything after it', () => {
    const result = filterByDatePreset(absoluteItems(), 'custom', dateOf, '2026-07-22', '', '');
    expect(result.map((i) => i.id).sort()).toEqual([2, 4]);
  });

  it('"custom" with only "to" keeps everything before end of that day', () => {
    const result = filterByDatePreset(absoluteItems(), 'custom', dateOf, '', '2026-07-22', '');
    expect(result.map((i) => i.id).sort()).toEqual([1, 3]);
  });

  it('"beforeCustomDate" keeps items strictly before the date', () => {
    const result = filterByDatePreset(absoluteItems(), 'beforeCustomDate', dateOf, '', '', '2026-07-22');
    expect(result.map((i) => i.id).sort()).toEqual([1, 3]);
  });

  it('"afterCustomDate" keeps items on or after the date', () => {
    const result = filterByDatePreset(absoluteItems(), 'afterCustomDate', dateOf, '', '', '2026-07-22');
    expect(result.map((i) => i.id).sort()).toEqual([2, 4]);
  });

  it('"onOrBeforeCustomDate" includes the whole selected day', () => {
    const result = filterByDatePreset(absoluteItems(), 'onOrBeforeCustomDate', dateOf, '', '', '2026-07-22');
    expect(result.map((i) => i.id).sort()).toEqual([1, 3]);
  });

  it('"onOrAfterCustomDate" keeps items on or after the date', () => {
    const result = filterByDatePreset(absoluteItems(), 'onOrAfterCustomDate', dateOf, '', '', '2026-07-22');
    expect(result.map((i) => i.id).sort()).toEqual([2, 4]);
  });

  it('does not drop items when an optional custom date is empty', () => {
    expect(filterByDatePreset(absoluteItems(), 'custom', dateOf, '', '', '').length).toBe(4);
    expect(filterByDatePreset(absoluteItems(), 'beforeCustomDate', dateOf, '', '', '').length).toBe(4);
    expect(filterByDatePreset(absoluteItems(), 'afterCustomDate', dateOf, '', '', '').length).toBe(4);
  });

  it('returns a new array and does not mutate the input', () => {
    const list = absoluteItems();
    const result = filterByDatePreset(list, 'today', dateOf, '', '', '');
    expect(result).not.toBe(list);
    expect(list.length).toBe(4);
  });
});

/**
 * Placement tests for the date-filter dropdown. We stub `getBoundingClientRect`
 * on fake trigger/scroll-root elements so no real layout is required.
 */
describe('computeDateDropdownPlacement', () => {
  /** Fake element whose rect spans [top, bottom] vertically. */
  const el = (top: number, bottom: number): HTMLElement =>
    ({
      getBoundingClientRect: () =>
        ({ top, bottom, left: 0, right: 0, width: 0, height: bottom - top }) as DOMRect,
    }) as unknown as HTMLElement;

  /** Scroll container visible from y=0 to y=600. */
  const scrollRoot = el(0, 600);

  it('opens downward when there is enough space below', () => {
    // Trigger at y=100..120 → 480px below, 100px above; dropdown needs 350.
    const placement = computeDateDropdownPlacement(el(100, 120), scrollRoot, 350);
    expect(placement.openUp).toBe(false);
    expect(placement.maxHeight).toBe(350); // natural height fits below
  });

  it('flips upward when there is more room above than below', () => {
    // Trigger at y=500..520 → 80px below, 500px above; dropdown needs 350.
    const placement = computeDateDropdownPlacement(el(500, 520), scrollRoot, 350);
    expect(placement.openUp).toBe(true);
    // Space above (500px) is plenty — the natural height caps the result.
    expect(placement.maxHeight).toBe(350);
  });

  it('stays downward when below has more room than above', () => {
    // Trigger at y=260..280 → 320px below, 260px above; dropdown needs 350.
    const placement = computeDateDropdownPlacement(el(260, 280), scrollRoot, 350);
    expect(placement.openUp).toBe(false);
    // Capped to spaceBelow minus the gap.
    expect(placement.maxHeight).toBe(320 - DATE_DROPDOWN_GAP);
  });

  it('clamps the height to the available space (internal scroll)', () => {
    // Trigger at y=100..120 → only 200px below; dropdown needs 350.
    const placement = computeDateDropdownPlacement(el(100, 120), el(0, 320), 350);
    expect(placement.openUp).toBe(false);
    expect(placement.maxHeight).toBe(200 - DATE_DROPDOWN_GAP);
  });

  it('never reports a height smaller than the gap', () => {
    // No room anywhere (trigger exactly fills the scroll root).
    const placement = computeDateDropdownPlacement(el(0, 600), scrollRoot, 350);
    expect(placement.maxHeight).toBeGreaterThanOrEqual(DATE_DROPDOWN_GAP);
  });
});

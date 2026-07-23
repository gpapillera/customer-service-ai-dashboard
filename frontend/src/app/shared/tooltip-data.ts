/** A single row in a rich tooltip card. */
export interface TooltipItem {
  label: string;
  value: string;
}

/** Data payload for the <code>csTooltip</code> directive. */
export interface TooltipData {
  items: TooltipItem[];
}

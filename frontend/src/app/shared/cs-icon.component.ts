import { Component, Input, OnChanges, OnInit, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { type LucideIconData } from 'lucide-angular';
import {
  ArrowLeft,
  Plus,
  Sparkles,
  SquarePen,
  Search,
  Ticket,
  Users,
  EllipsisVertical,
  Eye,
  Trash2,
  CircleUser,
  LogOut,
  FolderOpen,
  TriangleAlert,
  TrendingUp,
  ChevronRight,
  Headset,
  LayoutDashboard,
  Briefcase,
  Clock,
  AlarmClock,
  CheckCircle2,
  WandSparkles,
  CircleAlert,
  Inbox,
  X,
  Mail,
  MailCheck,
  Phone,
  ChevronLeft,
  Menu,
  Bell,
  BellRing,
  Settings,
  KeyRound,
  Pencil,
  Check,
  MessageSquare,
  Send,
} from 'lucide-angular/src/icons';

/**
 * Local, CDN-free icon component. Replaces <mat-icon> everywhere so the UI
 * can never break because of a missing external font. Templates keep using
 * the old Material ligature names (e.g. "add", "edit") and this component
 * maps them to bundled Lucide SVGs. Add new names to ICON_MAP as needed.
 */
const ICON_MAP: Record<string, LucideIconData> = {
  arrow_back: ArrowLeft,
  add: Plus,
  auto_awesome: Sparkles,
  wand: WandSparkles,
  edit: SquarePen,
  search: Search,
  confirmation_number: Ticket,
  people: Users,
  more_vert: EllipsisVertical,
  visibility: Eye,
  delete: Trash2,
  account_circle: CircleUser,
  logout: LogOut,
  folder_open: FolderOpen,
  priority_high: TriangleAlert,
  trending_up: TrendingUp,
  chevron_right: ChevronRight,
  headset: Headset,
  dashboard: LayoutDashboard,
  briefcase: Briefcase,
  clock: Clock,
  schedule: AlarmClock,
  check_circle: CheckCircle2,
  circle_alert: CircleAlert,
  inbox: Inbox,
  close: X,
  mail: Mail,
  phone: Phone,
  chevron_left: ChevronLeft,
  menu: Menu,
  notifications: Bell,
  notifications_active: BellRing,
  forum: MessageSquare,
  send: Send,
};

@Component({
  selector: 'cs-icon',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (svgHtml) {
      <span class="cs-icon-svg" [class]="cssClass" [innerHTML]="svgHtml" aria-hidden="true"></span>
    } @else {
      <!-- Unknown icon name: render nothing rather than broken glyph text. -->
      <span class="cs-icon-unknown" [attr data-name]="name" aria-hidden="true"></span>
    }
  `,
  styles: [
    `
      :host {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        vertical-align: middle;
        line-height: 0;
      }
      :host ::ng-deep svg {
        display: block;
        width: 1em;
        height: 1em;
      }
    `,
  ],
})
export class CsIconComponent implements OnInit, OnChanges {
  /** Material-style ligature name, e.g. "add", "edit". */
  @Input() name = '';
  /** Pixel size of the icon (default 20). */
  @Input() size = 20;
  /** Stroke width of the SVG (default 2). */
  @Input() strokeWidth = 2;
  /** Extra CSS classes forwarded to the underlying SVG element. */
  @Input('class') cssClass = '';

  /** Sanitized SVG markup for the resolved icon, or undefined if unknown. */
  svgHtml?: SafeHtml;

  constructor(private readonly sanitizer: DomSanitizer) {}

  ngOnInit(): void {
    this.render();
  }

  /** Re-render whenever an input (e.g. the icon `name`) changes, so the
      SVG updates instead of staying frozen on its first-rendered icon. */
  ngOnChanges(changes: SimpleChanges): void {
    if (changes['name'] || changes['size'] || changes['strokeWidth']) {
      this.render();
    }
  }

  private render(): void {
    const icon = ICON_MAP[this.name];
    if (!icon) {
      this.svgHtml = undefined;
      return;
    }
    const svg = CsIconComponent.renderSvg(icon, this.size, this.strokeWidth);
    this.svgHtml = this.sanitizer.bypassSecurityTrustHtml(svg);
  }

  /**
   * Builds a standalone <svg> string from Lucide icon node data.
   * LucideIconData is an array of [tag, attrs, children?] tuples.
   */
  private static renderSvg(icon: LucideIconData, size: number, strokeWidth: number): string {
    const body = icon.map((node) => CsIconComponent.renderNode(node)).join('');
    return (
      `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" ` +
      `viewBox="0 0 24 24" fill="none" stroke="currentColor" ` +
      `stroke-width="${strokeWidth}" stroke-linecap="round" stroke-linejoin="round">` +
      `${body}</svg>`
    );
  }

  private static renderNode(node: unknown): string {
    if (!Array.isArray(node)) {
      return '';
    }
    const [tag, attrs, children] = node as [string, Record<string, string>, unknown[]?];
    const attrStr = Object.entries(attrs ?? {})
      .map(([k, v]) => ` ${k}="${CsIconComponent.escapeAttr(v)}"`)
      .join('');
    const inner = Array.isArray(children)
      ? children.map((c) => CsIconComponent.renderNode(c)).join('')
      : '';
    return `<${tag}${attrStr}>${inner}</${tag}>`;
  }

  private static escapeAttr(value: string): string {
    return String(value)
      .replace(/&/g, '&amp;')
      .replace(/"/g, '&quot;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;');
  }
}

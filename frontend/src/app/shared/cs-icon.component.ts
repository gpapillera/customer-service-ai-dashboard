import { Component, Input, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { LucideAngularModule, type LucideIconData } from 'lucide-angular';
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
  ChevronRight,
  Headset,
  LayoutDashboard,
  Briefcase,
  Clock,
  CheckCircle2,
  WandSparkles,
  CircleAlert,
  Inbox,
  X,
  Mail,
  Phone,
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
  chevron_right: ChevronRight,
  headset: Headset,
  dashboard: LayoutDashboard,
  briefcase: Briefcase,
  clock: Clock,
  check_circle: CheckCircle2,
  circle_alert: CircleAlert,
  inbox: Inbox,
  close: X,
  mail: Mail,
  phone: Phone,
};

@Component({
  selector: 'cs-icon',
  standalone: true,
  imports: [CommonModule, LucideAngularModule],
  template: `
    @if (icon) {
      <i-lucide
        [img]="icon"
        [size]="size"
        [strokeWidth]="strokeWidth"
        [class]="cssClass"
        aria-hidden="true"
      ></i-lucide>
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
      }
    `,
  ],
})
export class CsIconComponent implements OnInit {
  /** Material-style ligature name, e.g. "add", "edit". */
  @Input() name = '';
  /** Pixel size of the icon (default 20). */
  @Input() size = 20;
  /** Stroke width of the SVG (default 2). */
  @Input() strokeWidth = 2;
  /** Extra CSS classes forwarded to the underlying SVG element. */
  @Input('class') cssClass = '';

  icon?: LucideIconData;

  ngOnInit(): void {
    this.icon = ICON_MAP[this.name];
  }
}

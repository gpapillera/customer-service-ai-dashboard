import { Component, effect, HostListener, inject, signal, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatIconModule } from '@angular/material/icon';
import { MatDialog } from '@angular/material/dialog';
import { BreakpointObserver } from '@angular/cdk/layout';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Title } from '@angular/platform-browser';
import { CsIconComponent } from '../cs-icon.component';
import { AuthService } from '../../auth/auth.service';
import { ThemeService } from '../theme.service';
import { ConfirmDialogComponent } from '../confirm-dialog.component';
import { NotificationBellComponent } from '../notification-bell.component';
import { StaffAccountPanelComponent } from '../staff-account-panel.component';
import { NavBadgeService } from '../nav-badge.service';
import { KbdNavDirective } from '../keyboard-nav.directive';

/**
 * Application shell: a white sidenav with navigation (active = light indigo
 * pill) and a Sign Out action pinned at the bottom. All guarded routes render
 * inside the <router-outlet>.
 * See docs/DIY.md §11 for the shell/layout/design-system walkthrough.
 */
@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [
    CommonModule,
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatSidenavModule,
    MatIconModule,
    CsIconComponent,
    NotificationBellComponent,
    StaffAccountPanelComponent,
    KbdNavDirective,
  ],
  templateUrl: './layout.component.html',
  styleUrl: './layout.component.scss',
})
export class LayoutComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly breakpointObserver = inject(BreakpointObserver);
  private readonly titleService = inject(Title);
  private readonly accountPanel = viewChild(StaffAccountPanelComponent);
  readonly navBadges = inject(NavBadgeService);
  readonly theme = inject(ThemeService);

  /** True on narrow viewports (<768px); the sidenav switches to overlay mode. */
  readonly isHandset = signal(false);
  /** Whether the sidenav is currently open (user toggle + auto-hide aware). */
  readonly opened = signal(true);
  /** Whether the settings panel (right slide-out) is open. */
  readonly settingsOpen = signal(false);
  /** True only for the brief moment after a user toggles the sidenav, so the
      page brand logo animates (enlarge/shrink) ONLY on an explicit toggle and
      never on plain route changes. */
  readonly brandAnimate = signal(false);

  readonly navLinks = [
    { path: '/dashboard', label: 'Dashboard', icon: 'dashboard' },
    { path: '/customers', label: 'Customers', icon: 'people' },
    { path: '/cases', label: 'Cases', icon: 'confirmation_number' },
    // Agents list is admin-only (Phase 5). Hidden entirely for Agent-role users.
    { path: '/agents', label: 'Agents', icon: 'supervisor_account', adminOnly: true },
    // Messages (conversations) tab is Agent-only (Phase 9).
    { path: '/messages', label: 'Messages', icon: 'forum', agentOnly: true },
    // Global conversations view is Admin-only (Phase 12).
    { path: '/conversations', label: 'Conversations', icon: 'forum', adminOnly: true },
    // Email log is visible to both Admin and Agent.
    { path: '/emails', label: 'Emails', icon: 'mail' },
  ];

  constructor() {
    // Start closed on narrow screens (auto-hide), open on desktop.
    const isNarrow =
      typeof window !== 'undefined' &&
      window.matchMedia('(max-width: 767px)').matches;
    this.isHandset.set(isNarrow);
    this.opened.set(!isNarrow);

    this.breakpointObserver
      .observe('(max-width: 767px)')
      .pipe(takeUntilDestroyed())
      .subscribe((state) => {
        this.isHandset.set(state.matches);
        // Shrinking to a small screen auto-hides the sidenav (rail shows).
        // Widening back to desktop must NOT force it open — respect the
        // user's manual toggle so a hidden sidenav stays hidden.
        if (state.matches) {
          this.opened.set(false);
        }
      });

    // Set the browser tab title to "{userName} - Customer Service" when the
    // user changes (login/logout). On logout the title reverts to "Customer Service".
    effect(() => {
      const user = this.auth.currentUser();
      const name = user?.fullName?.trim();
      this.titleService.setTitle(name ? `${name} - Customer Service` : 'Customer Service');
    });
  }

  /** Collapse/expand the sidenav (works in both side and overlay modes). */
  toggleSidenav(): void {
    this.opened.update((v) => !v);
    // Flag the brand-logo animation for the duration of the transition so it
    // only plays on an explicit toggle, not on route changes.
    this.brandAnimate.set(true);
    setTimeout(() => this.brandAnimate.set(false), 340);
  }

  /** Listen for global keyboard shortcuts (Ctrl+B to toggle sidenav, Escape to close on mobile). */
  @HostListener('document:keydown', ['$event'])
  handleGlobalShortcut(event: KeyboardEvent): void {
    // Ctrl+B / Cmd+B: toggle sidenav
    if ((event.ctrlKey || event.metaKey) && event.key === 'b') {
      event.preventDefault();
      this.toggleSidenav();
      return;
    }
    // Escape: close overlay sidenav on mobile
    if (event.key === 'Escape' && this.isHandset() && this.opened()) {
      this.toggleSidenav();
      return;
    }
  }

  /** Sync the open state when the sidenav is closed via its backdrop (overlay
      mode on small screens). Without this, a backdrop click closes the panel
      visually while `opened` stays true, hiding both the sidenav and the rail. */
  onSidenavOpenedChange(open: boolean): void {
    this.opened.set(open);
  }

  /** The currently signed-in user (or null). */
  get user() {
    return this.auth.currentUser();
  }

  /** Nav links visible to the current user (admin-only items filtered out for agents;
      agent-only items filtered out for admins). */
  get visibleNavLinks() {
    const role = this.auth.getRole();
    const isAdmin = role === 'Admin';
    const isAgent = role === 'Agent';
    return this.navLinks.filter(
      (l) => (!l.adminOnly || isAdmin) && (!l.agentOnly || isAgent),
    );
  }

  /** Opens the staff account panel. */
  openAccount(): void {
    this.accountPanel()?.show();
  }

  /** Open the settings slide-out panel. */
  openSettings(): void {
    this.settingsOpen.set(true);
  }

  /** Close the settings slide-out panel. */
  closeSettings(): void {
    this.settingsOpen.set(false);
  }

  /** Asks for confirmation, then logs the user out (only on confirm). */
  logout(): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        title: 'Sign out',
        message: 'Are you sure you want to sign out?',
        confirmText: 'Sign out',
        cancelText: 'Cancel',
        icon: 'logout',
      },
      width: '400px',
      maxWidth: '92vw',
      autoFocus: false,
    });
    ref.afterClosed().subscribe((confirmed) => {
      if (confirmed) {
        this.auth.logout();
        this.router.navigateByUrl('/login');
      }
    });
  }
}

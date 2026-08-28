import { Injectable, inject, signal, computed, DestroyRef, effect } from '@angular/core';
import { AuthService } from '../auth/auth.service';

/**
 * Real-time push over Server-Sent Events (SSE) for EVERY staff (and customer
 * self-service) mutation.
 *
 * Why fetch + ReadableStream (not EventSource): the browser EventSource API
 * cannot send custom auth headers, and our API authenticates the SSE stream
 * with the staff JWT Bearer token (same as every other /api request). A
 * streaming fetch carries the Authorization header, so we parse the SSE frames
 * ourselves.
 *
 * The backend emits a single unified `live-update` event for any mutation
 * (case assignment/status/priority/comment, customer profile/delete/restore,
 * or a customer editing their own portal profile). This service exposes it on
 * the `liveUpdate` signal so every data-bearing surface can refresh *instantly*
 * instead of waiting for the list poll. The poll stays as a fallback.
 *
 * Legacy shims `caseEvent` and `customerUpdate` are derived from `liveUpdate`
 * for code still reading them during the transition; migrate consumers to
 * `liveUpdate` and delete the shims once all are moved.
 *
 * Reconnects with a capped exponential backoff if the connection drops. One
 * connection per browser tab (providedIn: 'root').
 */
export interface LiveUpdateEvent {
  kind: string; // 'case-assignment' | 'case-update' | 'customer-update' | 'customer-deleted' | 'customer-restored'
  caseId?: number | null;
  customerId?: number | null;
  actorUserId?: string | null;
  actorRole?: string | null;
  assignedToUserId?: string | null;
}

/** @deprecated derive from `liveUpdate`; kept during migration. */
export interface CaseEvent {
  caseId: number;
  assignedToUserId: string | null;
  type: string;
}

/** @deprecated derive from `liveUpdate`; kept during migration. */
export interface CustomerUpdateEvent {
  customerId: number;
  actorUserId: string | null;
  actorRole: string | null;
}

@Injectable({ providedIn: 'root' })
export class RealtimeService {
  private readonly auth = inject(AuthService);

  /** Emits the latest unified mutation event the moment it arrives (or null on connect). */
  readonly liveUpdate = signal<LiveUpdateEvent | null>(null);

  /** @deprecated use `liveUpdate()`; kept for consumers not yet migrated. */
  readonly caseEvent = computed<CaseEvent | null>(() => {
    const e = this.liveUpdate();
    if (e?.kind !== 'case-assignment') return null;
    return { caseId: e.caseId ?? 0, assignedToUserId: e.assignedToUserId ?? null, type: 'assignment' };
  });

  /** @deprecated use `liveUpdate()`; kept for consumers not yet migrated. */
  readonly customerUpdate = computed<CustomerUpdateEvent | null>(() => {
    const e = this.liveUpdate();
    if (e?.kind !== 'customer-update') return null;
    return { customerId: e.customerId ?? 0, actorUserId: e.actorUserId ?? null, actorRole: e.actorRole ?? null };
  });

  /** Connection health, for diagnostics/UI. */
  readonly connected = signal(false);

  private controller: AbortController | null = null;
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null;
  private backoffMs = 1000;
  private readonly maxBackoffMs = 5_000;
  private started = false;

  constructor() {
    // Tear everything down when the root scope is destroyed (app close).
    inject(DestroyRef).onDestroy(() => this.stop());
    // Auto-open the SSE stream once a user is authenticated. The service is
    // providedIn:'root' and injected by every assignment-aware surface, but its
    // constructor can run BEFORE AuthService.setSession() populates the current
    // user (e.g. at bootstrap, or on a cold reload where sessionStorage is read
    // async). React to the auth signal so the connection opens the moment a user
    // is present — and re-opens after a logout/login swap. This effect only
    // *initiates* an async connect (no signal writes inside it), so it's NG0600-safe.
    effect(() => {
      if (this.auth.currentUser()) {
        this.start();
      }
    });
  }

  /** Starts the SSE connection if not already running. Idempotent. */
  start(): void {
    if (this.started) return;
    if (!this.auth.isAuthenticated()) return;
    this.started = true;
    this.connect();
  }

  private connect(): void {
    // The access JWT is an HttpOnly cookie (set by the API on login/refresh) that
    // JS cannot read, so getToken() returns null by design. Authentication for the
    // SSE stream is carried by that cookie via `credentials: 'include'` (the API's
    // JwtBearer handler reads the cookie — dual-source with the Authorization
    // header). We therefore do NOT require a header token here; if a legacy token
    // is somehow present we attach it as a bonus, but its absence must NOT block
    // the connection (that was the bug: getToken()===null made connect() bail and
    // the live push never arrived).
    const token = this.auth.getToken(); // null for HttpOnly-cookie auth (normal)
    this.controller = new AbortController();
    const headers: Record<string, string> = { Accept: 'text/event-stream' };
    if (token) headers['Authorization'] = `Bearer ${token}`;
    fetch('/api/cases/events', {
      // Send cookies so the access_token cookie authenticates the SSE stream.
      credentials: 'include',
      headers,
      signal: this.controller.signal,
    })
      .then((res) => {
        if (!res.ok || !res.body) {
          this.scheduleReconnect();
          return;
        }
        this.connected.set(true);
        this.backoffMs = 1000; // reset on successful connect
        const reader = res.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';
        const pump = (): Promise<void> =>
          reader.read().then(
            ({ value, done }) => {
              if (done) {
                this.scheduleReconnect();
                return;
              }
              buffer += decoder.decode(value, { stream: true });
              // SSE frames are separated by a blank line.
              let idx: number;
              while ((idx = buffer.indexOf('\n\n')) !== -1) {
                const frame = buffer.slice(0, idx);
                buffer = buffer.slice(idx + 2);
                this.handleFrame(frame);
              }
              return pump();
            },
            // A read rejection (background-tab throttle, transient blip, token
            // hiccup) MUST schedule a reconnect — without this handler the
            // rejection is unhandled, the stream dies silently, and the app
            // falls back to the 30s poll forever ("instant at first, then
            // wears out"). This is the recovery path.
            () => this.scheduleReconnect(),
          );
        return pump();
      })
      .catch(() => this.scheduleReconnect());
  }

  private handleFrame(frame: string): void {
    // Normalize CRLF/LF line endings (SSE frames may arrive with \r\n).
    const normalized = frame.replace(/\r\n/g, '\n').trim();
    const eventMatch = normalized.match(/^event:\s*(.+)$/m);
    const dataMatch = normalized.match(/^data:\s*(.+)$/m);
    const eventName = eventMatch?.[1]?.trim();
    const data = dataMatch?.[1]?.trim();
    if ((eventName === 'live-update' || eventName === 'case-assignment') && data) {
      try {
        const evt = JSON.parse(data) as LiveUpdateEvent;
        this.liveUpdate.set(evt);
      } catch {
        // Malformed frame — ignore.
      }
    }
  }

  private scheduleReconnect(): void {
    this.connected.set(false);
    if (this.reconnectTimer) clearTimeout(this.reconnectTimer);
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = null;
      this.connect();
    }, this.backoffMs);
    this.backoffMs = Math.min(this.backoffMs * 2, this.maxBackoffMs);
  }

  private stop(): void {
    this.started = false;
    this.controller?.abort();
    this.controller = null;
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }
    this.connected.set(false);
  }
}

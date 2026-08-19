import { Injectable, inject, signal, DestroyRef } from '@angular/core';
import { AuthService } from '../auth/auth.service';

/**
 * Real-time case-assignment push over Server-Sent Events (SSE).
 *
 * Why fetch + ReadableStream (not EventSource): the browser EventSource API
 * cannot send custom auth headers, and our API authenticates the SSE stream
 * with the staff JWT Bearer token (same as every other /api request). A
 * streaming fetch carries the Authorization header, so we parse the SSE frames
 * ourselves.
 *
 * On every assignment change (assign / reassign / unassign) the backend pushes
 * a `case-assignment` event; this service emits it on the `caseEvent` signal so
 * subscribers (agent "My Cases" list, dashboard, case detail) can refresh
 * *instantly* instead of waiting for the 30s poll. The poll stays as a fallback.
 *
 * Reconnects with a capped exponential backoff if the connection drops. One
 * connection per browser tab (providedIn: 'root').
 */
export interface CaseEvent {
  caseId: number;
  assignedToUserId: string | null;
  type: string;
}

@Injectable({ providedIn: 'root' })
export class RealtimeService {
  private readonly auth = inject(AuthService);

  /** Emits the latest case-assignment event the moment it arrives (or null on connect). */
  readonly caseEvent = signal<CaseEvent | null>(null);

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
    // Auto-open the stream on first use. The service is providedIn: 'root' and
    // injected by every assignment-aware surface (nav badges, case list,
    // dashboard, conversations, case detail), so the push is live no matter
    // which page the user opens first. Idempotent.
    this.start();
  }

  /** Starts the SSE connection if not already running. Idempotent. */
  start(): void {
    if (this.started) return;
    if (!this.auth.isAuthenticated()) return;
    this.started = true;
    this.connect();
  }

  private connect(): void {
    const token = this.auth.getToken();
    if (!token) {
      this.started = false;
      return;
    }
    this.controller = new AbortController();
    fetch('/api/cases/events', {
      // Send cookies so the access_token cookie authenticates the SSE stream
      // (the API reads JWT from cookie OR Authorization header — dual-source).
      credentials: 'include',
      headers: { Authorization: `Bearer ${token}`, Accept: 'text/event-stream' },
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
    const eventMatch = frame.match(/event:\s*(.+)/);
    const dataMatch = frame.match(/data:\s*(.+)/);
    const eventName = eventMatch?.[1]?.trim();
    const data = dataMatch?.[1]?.trim();
    if (eventName === 'case-assignment' && data) {
      try {
        const evt = JSON.parse(data) as CaseEvent;
        this.caseEvent.set(evt);
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

import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatIconModule } from '@angular/material/icon';
import { CsIconComponent } from '../shared/cs-icon.component';
import { CustomerService } from './customer.service';
import { CustomerCaseDetail, CustomerCaseComment } from '../shared/models';

/**
 * Customer case detail. Shows the case subject/description/status and the
 * shared comment thread. Deliberately renders NO priority / AI-prediction /
 * call-log / assigned-agent data — the DTO carries none of it.
 */
@Component({
  selector: 'app-my-case-detail',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    ReactiveFormsModule,
    MatButtonModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatIconModule,
    CsIconComponent,
  ],
  templateUrl: './my-case-detail.component.html',
  styleUrl: './my-case-detail.component.scss',
})
export class MyCaseDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);
  private readonly customerService = inject(CustomerService);

  readonly detail = signal<CustomerCaseDetail | null>(null);
  readonly comments = signal<CustomerCaseComment[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly commentForm = this.fb.nonNullable.group({
    body: ['', [Validators.required, Validators.maxLength(4000)]],
  });
  readonly sending = signal(false);
  readonly commentError = signal<string | null>(null);

  private caseId = 0;

  ngOnInit(): void {
    const idParam = this.route.snapshot.paramMap.get('id');
    this.caseId = Number(idParam);
    if (!idParam || Number.isNaN(this.caseId)) {
      this.loading.set(false);
      this.error.set('Invalid case.');
      return;
    }
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.customerService.getCase(this.caseId).subscribe({
      next: (d) => {
        this.detail.set(d);
        this.loading.set(false);
        this.loadComments();
      },
      error: () => {
        this.loading.set(false);
        this.error.set('We could not open this case.');
      },
    });
  }

  loadComments(): void {
    this.customerService.getComments(this.caseId).subscribe({
      next: (list) => this.comments.set(list),
      error: () => this.commentError.set('Could not load the conversation.'),
    });
  }

  sendComment(): void {
    if (this.commentForm.invalid) {
      this.commentForm.markAllAsTouched();
      return;
    }
    const body = this.commentForm.getRawValue().body.trim();
    if (!body) {
      return;
    }
    this.sending.set(true);
    this.commentError.set(null);
    this.customerService.addComment(this.caseId, { body }).subscribe({
      next: (posted) => {
        this.comments.update((list) => [...list, posted]);
        this.commentForm.reset();
        this.sending.set(false);
      },
      error: () => {
        this.sending.set(false);
        this.commentError.set('We could not send your message. Try again.');
      },
    });
  }

  back(): void {
    this.router.navigateByUrl('/customer/cases');
  }

  statusClass(s: string): string {
    return 'status-' + s.toLowerCase();
  }

  formatDate(value: string): string {
    return new Date(value).toLocaleString();
  }
}

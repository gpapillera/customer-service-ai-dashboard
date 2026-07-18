import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatInputModule } from '@angular/material/input';
import { CsIconComponent } from '../../shared/cs-icon.component';

/**
 * Reusable Material 3 search + filter toolbar (Row A of the Cases page).
 * Emits each field's value as the user changes it; the parent owns the
 * actual filtering logic. Query-param pre-fill is supported via the
 * `search` / `status` / `priority` / `category` inputs (patched into the form).
 */
@Component({
  selector: 'app-search-filter-toolbar',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatSelectModule,
    MatInputModule,
    CsIconComponent,
  ],
  templateUrl: './search-filter-toolbar.component.html',
  styleUrl: './search-filter-toolbar.component.scss',
})
export class SearchFilterToolbarComponent implements OnChanges {
  /** Option lists supplied by the parent. */
  @Input() statuses: string[] = [];
  @Input() priorities: string[] = [];
  @Input() categories: string[] = [];

  /** Current values (used for query-param pre-fill). Empty string = "All …". */
  @Input() search = '';
  @Input() status = '';
  @Input() priority = '';
  @Input() category = '';

  @Output() searchChanged = new EventEmitter<string>();
  @Output() statusChanged = new EventEmitter<string>();
  @Output() priorityChanged = new EventEmitter<string>();
  @Output() categoryChanged = new EventEmitter<string>();

  readonly form: FormGroup;

  constructor(private readonly fb: FormBuilder) {
    this.form = this.fb.group({
      search: [''],
      status: [''],
      priority: [''],
      category: [''],
    });

    this.form.get('search')?.valueChanges.subscribe((v: string) => this.searchChanged.emit(v ?? ''));
    this.form.get('status')?.valueChanges.subscribe((v: string) => this.statusChanged.emit(v ?? ''));
    this.form.get('priority')?.valueChanges.subscribe((v: string) => this.priorityChanged.emit(v ?? ''));
    this.form.get('category')?.valueChanges.subscribe((v: string) => this.categoryChanged.emit(v ?? ''));
  }

  /** Patch incoming input values (e.g. from query params) into the form. */
  ngOnChanges(changes: SimpleChanges): void {
    const patch: Record<string, unknown> = {};
    if (changes['search']) patch['search'] = this.search ?? '';
    if (changes['status']) patch['status'] = this.status ?? '';
    if (changes['priority']) patch['priority'] = this.priority ?? '';
    if (changes['category']) patch['category'] = this.category ?? '';
    if (Object.keys(patch).length) {
      this.form.patchValue(patch, { emitEvent: false });
    }
  }
}

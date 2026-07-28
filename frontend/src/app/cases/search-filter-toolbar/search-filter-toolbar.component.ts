import { Component, EventEmitter, Input, OnChanges, Output, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { CsIconComponent } from '../../shared/cs-icon.component';

/**
 * Search toolbar (Row A of the Cases page) — search bar + toggle buttons.
 * Filtering for status/priority/category is done via inline dropdowns in
 * the table headers.
 */
@Component({
  selector: 'app-search-filter-toolbar',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    CsIconComponent,
  ],
  templateUrl: './search-filter-toolbar.component.html',
  styleUrl: './search-filter-toolbar.component.scss',
})
export class SearchFilterToolbarComponent implements OnChanges {
  /** Current search value (used for query-param pre-fill). */
  @Input() search = '';

  /** Toggle states. */
  @Input() aiActive = false;
  @Input() overdueActive = false;

  @Output() searchChanged = new EventEmitter<string>();
  @Output() aiToggled = new EventEmitter<void>();
  @Output() overdueToggled = new EventEmitter<void>();

  readonly form: FormGroup;

  constructor(private readonly fb: FormBuilder) {
    this.form = this.fb.group({
      search: [''],
    });

    this.form.get('search')?.valueChanges.subscribe((v: string) => this.searchChanged.emit(v ?? ''));
  }

  /** Patch incoming input value (e.g. from query params) into the form. */
  ngOnChanges(changes: SimpleChanges): void {
    if (changes['search']) {
      this.form.patchValue({ search: this.search ?? '' }, { emitEvent: false });
    }
  }
}

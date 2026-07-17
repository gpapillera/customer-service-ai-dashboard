import { Category } from './models';

/**
 * Category lookup matching the backend seed data (Ids 1-5). The API does not
 * yet expose a categories endpoint, so this constant keeps the frontend forms
 * in sync with the seeded categories. Update here if seed categories change.
 */
export const CATEGORIES: Category[] = [
  { id: 1, name: 'Billing', description: 'Invoices, payments, refunds' },
  { id: 2, name: 'Shipping', description: 'Delivery, tracking, logistics' },
  { id: 3, name: 'Technical', description: 'Bugs, outages, integration' },
  { id: 4, name: 'Account', description: 'Login, profile, access' },
  { id: 5, name: 'Product', description: 'Features, returns, warranty' },
];

/** Returns a category name by id, or 'Unknown' if not found. */
export function categoryName(id: number): string {
  return CATEGORIES.find((c) => c.id === id)?.name ?? 'Unknown';
}

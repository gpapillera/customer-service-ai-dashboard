"""Data cleaning utilities for the Customer Service AI Dashboard.

This module ingests a raw customer-service export (simulating a Dynamics 365 /
Excel dump), then applies the cleaning pipeline described in the project's
MVP build prompt (Section 9):

    1. Drop exact duplicate rows.
    2. Normalize phone numbers (strip non-digits, consistent format).
    3. Normalize emails (lowercase, trim whitespace).
    4. Parse inconsistent date formats into ISO 8601 (YYYY-MM-DD).
    5. Fill missing ``Category`` with ``"Uncategorized"``.
    6. Trim whitespace from all text fields.
    7. Flag (don't silently drop) rows with missing required fields
       (Customer Name, Case Subject) into ``rejected_rows.csv``.
    8. Write the cleaned result to ``ml/data/cleaned/cases_cleaned.csv`` and
       log a short summary (rows in, rows out, rows rejected) to the console.

Example raw row (messy):
    Customer Name,Email,Phone,Case Subject,Description,Category,Date Created,Status
    juan dela cruz,  JUAN@ACME.PH,(0917) 123 4567 ,double charge,billed twice,billing,2024/01/05,Open
    juan dela cruz,Juan@acme.ph,09171234567,double charge,billed twice,Billing,2024-01-05,Open
"""

from __future__ import annotations

import argparse
import csv
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

# Canonical category vocabulary; raw values are fuzzy-matched to these.
KNOWN_CATEGORIES = {
    "billing": "Billing",
    "invoice": "Billing",
    "payment": "Billing",
    "refund": "Billing",
    "shipping": "Shipping / Supply Chain",
    "delivery": "Shipping / Supply Chain",
    "logistics": "Shipping / Supply Chain",
    "tracking": "Shipping / Supply Chain",
    "supply": "Shipping / Supply Chain",
    "technical": "Technical Support",
    "tech": "Technical Support",
    "bug": "Technical Support",
    "outage": "Technical Support",
    "integration": "Technical Support",
    "account": "Account",
    "login": "Account",
    "password": "Account",
    "access": "Account",
    "product": "Product Quality",
    "warranty": "Product Quality",
    "return": "Product Quality",
    "feature": "Product Quality",
    "general": "General Inquiry",
    "inquiry": "General Inquiry",
    "question": "General Inquiry",
}

DEFAULT_CATEGORY = "Uncategorized"

# Fields that must be present for a row to be usable downstream.
REQUIRED_FIELDS = ("customer_name", "case_subject")


@dataclass
class RawCase:
    """A single raw case row parsed from the source CSV.

    Attributes:
        customer_name: Free-text customer name.
        email: Free-text email (any casing / spacing).
        phone: Free-text phone number.
        case_subject: Case subject line.
        description: Free-text case description.
        category: Free-text category label.
        date_created: Free-text created date.
        status: Free-text status label.
    """

    customer_name: str
    email: str
    phone: str
    case_subject: str
    description: str
    category: str
    date_created: str
    status: str


@dataclass
class CleanedCase:
    """A normalized case row ready for downstream use.

    Attributes:
        customer_name: Title-cased customer name.
        email: Lowercased, trimmed email (may be empty).
        phone: Normalized phone digits (may be empty).
        case_subject: Trimmed subject.
        description: Trimmed description (may be empty).
        category: Canonical category name.
        date_created: ISO 8601 date (YYYY-MM-DD), or empty if unparseable.
        status: Trimmed status (may be empty).
    """

    customer_name: str
    email: str
    phone: str
    case_subject: str
    description: str
    category: str
    date_created: str
    status: str


def normalize_email(email: str) -> str:
    """Normalize an email address to lowercase, trimmed form.

    Args:
        email: Raw email string (may contain surrounding whitespace).

    Returns:
        Lowercased, stripped email, or empty string when input is blank.
    """
    if not email:
        return ""
    return email.strip().lower()


def normalize_phone(phone: str) -> str:
    """Normalize a phone number to E.164-ish digits, preserving a leading '+'.

    Strips all non-digit characters except an optional leading plus sign. A
    local Philippine number such as ``(0917) 123 4567`` becomes ``09171234567``.

    Args:
        phone: Raw phone string.

    Returns:
        Normalized phone string, or empty string when input is blank.
    """
    if not phone:
        return ""
    phone = phone.strip()
    has_plus = phone.startswith("+")
    digits = re.sub(r"\D", "", phone)
    return ("+" + digits) if has_plus else digits


def normalize_category(category: str) -> str:
    """Map a raw category label to a canonical vocabulary entry.

    Performs case-insensitive substring matching against KNOWN_CATEGORIES.
    Unrecognized values fall back to DEFAULT_CATEGORY.

    Args:
        category: Raw category label from the source system.

    Returns:
        A canonical category name from the vocabulary.
    """
    if not category:
        return DEFAULT_CATEGORY
    key = category.strip().lower()
    if key in KNOWN_CATEGORIES:
        return KNOWN_CATEGORIES[key]
    for token, canonical in KNOWN_CATEGORIES.items():
        if token in key:
            return canonical
    return DEFAULT_CATEGORY


def parse_date(value: str) -> str:
    """Parse a messy date string into ISO ``YYYY-MM-DD`` format.

    Tries several common separators (``/``, ``.``, ``-``) and year-first or
    day-first orderings. Returns an empty string when parsing fails.

    Args:
        value: Raw date string (e.g. ``2024/01/05``, ``05-01-2024``).

    Returns:
        ISO date string, or empty string on failure.
    """
    if not value:
        return ""
    value = value.strip()
    for sep in ("/", ".", "-", " "):
        if sep in value:
            parts = re.split(re.escape(sep), value)
            if len(parts) == 3:
                a, b, c = (p.zfill(2) for p in parts)
                # Heuristic: a 4-digit token is the year.
                if len(c) == 4:
                    return f"{c}-{a}-{b}"
                if len(a) == 4:
                    return f"{a}-{b}-{c}"
                # Default to day-month-year (common in PH exports).
                return f"{c[:4] if len(c) == 4 else '0000'}-{b}-{a}"
    return ""


def dedupe(rows: Iterable[RawCase]) -> list[RawCase]:
    """Remove exact duplicate rows based on all field values.

    When duplicates exist, the first occurrence wins (assumed most complete).

    Args:
        rows: Iterable of parsed raw cases.

    Returns:
        Deduplicated list of RawCase objects, preserving input order.
    """
    seen: set[tuple[str, ...]] = set()
    result: list[RawCase] = []
    for row in rows:
        # Compare parsed dates so "2024/01/05" and "2024-01-05" count as equal.
        key = (
            row.customer_name.lower(),
            normalize_email(row.email),
            normalize_phone(row.phone),
            row.case_subject.lower(),
            row.description.lower(),
            row.category.lower(),
            parse_date(row.date_created),
            row.status.lower(),
        )
        if key in seen:
            continue
        seen.add(key)
        result.append(row)
    return result


def read_raw(path: Path) -> list[RawCase]:
    """Read a raw CSV export into RawCase records.

    Column names are matched case-insensitively against the canonical set
    (Customer Name, Email, Phone, Case Subject, Description, Category,
    Date Created, Status).

    Args:
        path: Path to the source CSV file.

    Returns:
        List of RawCase objects.

    Raises:
        FileNotFoundError: If the source file does not exist.
    """
    if not path.exists():
        raise FileNotFoundError(f"Input file not found: {path}")
    rows: list[RawCase] = []
    with path.open(newline="", encoding="utf-8-sig") as fh:
        reader = csv.DictReader(fh)
        for r in reader:
            get = lambda k: (r.get(k) or r.get(k.lower()) or "").strip()
            rows.append(
                RawCase(
                    customer_name=get("Customer Name"),
                    email=r.get("Email") or r.get("email") or "",
                    phone=r.get("Phone") or r.get("phone") or "",
                    case_subject=get("Case Subject"),
                    description=get("Description"),
                    category=r.get("Category") or r.get("category") or "",
                    date_created=r.get("Date Created") or r.get("date_created") or "",
                    status=get("Status"),
                )
            )
    return rows


def clean(rows: list[RawCase]) -> tuple[list[CleanedCase], list[RawCase]]:
    """Apply all normalization steps and split valid vs. rejected rows.

    Rows missing any REQUIRED_FIELDS value are returned separately so they can
    be written to ``rejected_rows.csv`` for manual review (never silently
    dropped).

    Args:
        rows: Raw parsed rows (already de-duplicated by the caller).

    Returns:
        A tuple of (cleaned_rows, rejected_rows).
    """
    cleaned: list[CleanedCase] = []
    rejected: list[RawCase] = []
    for row in dedupe(rows):
        if not row.customer_name or not row.case_subject:
            rejected.append(row)
            continue
        cleaned.append(
            CleanedCase(
                customer_name=row.customer_name.title(),
                email=normalize_email(row.email),
                phone=normalize_phone(row.phone),
                case_subject=row.case_subject.strip(),
                description=row.description.strip(),
                category=normalize_category(row.category),
                date_created=parse_date(row.date_created),
                status=row.status.strip(),
            )
        )
    return cleaned, rejected


def write_csv(rows: list, path: Path, fieldnames: list[str]) -> None:
    """Write rows (dataclass instances) to a CSV file.

    Args:
        rows: Row objects with the given field names as attributes.
        path: Destination CSV path (parent dirs created as needed).
        fieldnames: Ordered list of attribute names to serialize.
    """
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", newline="", encoding="utf-8") as fh:
        writer = csv.DictWriter(fh, fieldnames=fieldnames)
        writer.writeheader()
        for row in rows:
            writer.writerow({f: getattr(row, f) for f in fieldnames})


def main() -> None:
    """CLI entry point: clean a raw CSV and write normalized + rejected outputs."""
    parser = argparse.ArgumentParser(description="Clean a raw customer-service CSV export.")
    parser.add_argument(
        "--input",
        default="ml/data/raw_cases.csv",
        help="Path to the raw CSV export.",
    )
    parser.add_argument(
        "--output",
        default="ml/data/cleaned/cases_cleaned.csv",
        help="Path to the cleaned CSV output.",
    )
    parser.add_argument(
        "--rejected",
        default="ml/data/cleaned/rejected_rows.csv",
        help="Path to the rejected-rows CSV (missing required fields).",
    )
    args = parser.parse_args()

    raw = read_raw(Path(args.input))
    cleaned, rejected = clean(raw)

    cleaned_fields = [
        "customer_name", "email", "phone", "case_subject",
        "description", "category", "date_created", "status",
    ]
    rejected_fields = [
        "customer_name", "email", "phone", "case_subject",
        "description", "category", "date_created", "status",
    ]

    write_csv(cleaned, Path(args.output), cleaned_fields)
    write_csv(rejected, Path(args.rejected), rejected_fields)

    print(
        "Cleaning summary:\n"
        f"  rows in      : {len(raw)}\n"
        f"  unique rows  : {len(dedupe(raw))} (after exact-dup drop)\n"
        f"  cleaned out  : {len(cleaned)}\n"
        f"  rejected     : {len(rejected)} (missing required fields)\n"
        f"  -> cleaned   : {args.output}\n"
        f"  -> rejected  : {args.rejected}"
    )


if __name__ == "__main__":
    main()

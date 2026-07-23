"""Export real case data from the database for ML training.

Connects to the SQLite database (or SQL Server via ``--mssql``), extracts all
cases with their computed features, and writes a CSV that ``train_model.py`` can
consume via ``--data``.

Usage:
    # Export from SQLite (switch Database:Provider to Sqlite first)
    python ml/export_training_data.py --db customer_service.db -o ml/data/training_data.csv

    # Export from SQL Server (requires pymssql)
    python ml/export_training_data.py --mssql -o ml/data/training_data.csv

Features exported (matching the ONNX model's 4-float input):
    - category_id         : Case.CategoryId (integer 1-7)
    - prior_case_count    : Number of prior cases from the same customer
    - days_since_contact  : Days since LastContactUtc (or CreatedAtUtc if null)
    - sentiment           : Lexicon-based score in [-1, 1] (same as train_model.py)
    - priority            : Label — Low | Medium | High
"""

from __future__ import annotations

import argparse
import csv
import os
import sqlite3
from datetime import datetime, timezone
from pathlib import Path

# Reuse the sentiment scorer from the training pipeline.
try:
    from train_model import sentiment_score, LABELS, LABEL_INDEX
except ImportError:
    # Fallback definitions when run standalone.
    LABELS = ["Low", "Medium", "High"]
    LABEL_INDEX = {l: i for i, l in enumerate(LABELS)}

    NEGATIVE_LEXICON = {
        "urgent": 2.0, "asap": 2.0, "immediately": 1.5, "broken": 1.5, "error": 1.5,
        "fail": 1.5, "failed": 1.5, "complaint": 2.0, "angry": 2.0, "furious": 2.5,
        "unacceptable": 2.0, "refund": 1.0, "chargeback": 1.5, "lawsuit": 2.5,
        "escalate": 1.5, "critical": 2.0, "down": 1.0, "outage": 1.5, "lost": 1.0,
        "missing": 1.0, "terrible": 2.0, "worst": 2.0, "hate": 2.0, "disappointed": 1.5,
        "frustrated": 1.5, "useless": 1.5, "scam": 2.0, "ripoff": 2.0, "bug": 1.0,
        "crash": 1.5, "denied": 1.0, "wrong": 0.8, "cancel": 0.5, "problem": 0.5,
        "issue": 0.3, "slow": 0.5, "late": 0.5, "never": 0.5,
    }
    POSITIVE_LEXICON = {
        "thank": 1.0, "thanks": 1.0, "appreciate": 1.5, "happy": 1.5, "great": 1.0,
        "excellent": 1.5, "love": 1.5, "resolved": 1.0, "solved": 1.0, "fixed": 1.0,
        "good": 0.8, "perfect": 1.5, "satisfied": 1.5, "wonderful": 1.5, "amazing": 1.5,
        "helpful": 1.0, "please": 0.3, "kind": 1.0, "quickly": 0.5, "works": 0.5,
        "working": 0.5, "glad": 1.0, "pleased": 1.5,
    }

    def sentiment_score(text: str) -> float:
        low = (text or "").lower()
        neg = sum(w for kw, w in NEGATIVE_LEXICON.items() if kw in low)
        pos = sum(w for kw, w in POSITIVE_LEXICON.items() if kw in low)
        total = pos + neg
        if total == 0:
            return 0.0
        return (pos - neg) / total


PRIORITY_MAP = {0: "Low", 1: "Medium", 2: "High"}


def parse_utc(value: str) -> datetime | None:
    """Parse a ISO‑8601 datetime string to a timezone-aware datetime."""
    if not value:
        return None
    try:
        dt = datetime.fromisoformat(value)
        if dt.tzinfo is None:
            dt = dt.replace(tzinfo=timezone.utc)
        return dt
    except (ValueError, TypeError):
        return None


def export_sqlite(db_path: str, output: str) -> int:
    """Export training data from a SQLite database.

    Args:
        db_path: Path to the SQLite database file.
        output: Path for the output CSV.

    Returns:
        Number of rows exported.
    """
    if not os.path.isfile(db_path):
        print(f"Error: database file not found — {db_path}")
        print("Hint: set Database:Provider to Sqlite in appsettings, then run "
              "the backend once to create and seed the database.")
        return 0

    conn = sqlite3.connect(db_path)
    conn.row_factory = sqlite3.Row
    cursor = conn.cursor()

    # Fetch all cases with related data.
    cursor.execute("""
        SELECT c.Id, c.Subject, c.Description, c.CategoryId, c.Priority,
               c.CustomerId, c.CreatedAtUtc, c.LastContactUtc,
               cust.Name AS CustomerName
        FROM Cases c
        JOIN Customers cust ON cust.Id = c.CustomerId
        ORDER BY c.CustomerId, c.CreatedAtUtc
    """)
    rows = cursor.fetchall()
    conn.close()

    if not rows:
        print("No cases found in the database. Seed some data first.")
        return 0

    # Build prior-case counts: for each customer, count earlier cases.
    prior_counts: dict[int, int] = {}
    output_rows: list[dict[str, str | int | float]] = []

    for row in rows:
        customer_id = row["CustomerId"]
        prior_case_count = prior_counts.get(customer_id, 0)
        prior_counts[customer_id] = prior_case_count + 1

        category_id = row["CategoryId"]

        # Compute days since last contact.
        now = datetime.now(timezone.utc)
        last_contact = parse_utc(row["LastContactUtc"])
        if last_contact is None:
            created = parse_utc(row["CreatedAtUtc"])
            if created:
                days_since = max(0, (now - created).days)
            else:
                days_since = 0
        else:
            days_since = max(0, (now - last_contact).days)

        # Compute sentiment from description + subject.
        text = f"{row['Subject'] or ''} {row['Description'] or ''}"
        sentiment = sentiment_score(text)

        # Map priority integer to label string.
        priority_int = row["Priority"]
        priority_label = PRIORITY_MAP.get(priority_int, "Medium")

        output_rows.append({
            "category_id": category_id,
            "prior_case_count": prior_case_count,
            "days_since_contact": days_since,
            "sentiment": round(sentiment, 4),
            "priority": priority_label,
        })

    # Write CSV.
    out_path = Path(output)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    with open(out_path, "w", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=[
            "category_id", "prior_case_count", "days_since_contact",
            "sentiment", "priority",
        ])
        writer.writeheader()
        writer.writerows(output_rows)

    print(f"Exported {len(output_rows)} training rows → {out_path}")
    print(f"  Priority distribution:")
    for label in LABELS:
        count = sum(1 for r in output_rows if r["priority"] == label)
        print(f"    {label}: {count} ({count / len(output_rows) * 100:.1f}%)")

    return len(output_rows)


def export_mssql(output: str) -> int:
    """Export training data from SQL Server (requires pymssql)."""
    try:
        import pymssql
    except ImportError:
        print("pymssql is required for SQL Server export. Install with:")
        print("  pip install pymssql")
        print("Or use SQLite instead.")
        return 0

    # Read connection details from appsettings.json.
    import json
    settings_path = Path(__file__).resolve().parent.parent / "backend" / "src" / "CustomerService.Api" / "appsettings.json"
    with open(settings_path) as f:
        config = json.load(f)
    cs = config.get("ConnectionStrings", {}).get("SqlServer", "")
    # Parse simple key=value;value pairs from the connection string.
    parts = {}
    for segment in cs.split(";"):
        if "=" in segment:
            k, v = segment.split("=", 1)
            parts[k.strip().lower()] = v.strip()

    host_port = parts.get("server", "localhost,1433").split(",")
    host = host_port[0]
    port = int(host_port[1]) if len(host_port) > 1 else 1433
    user = parts.get("user id", "sa")
    password = parts.get("password", "")
    database = parts.get("database", "CustomerServiceDb")

    print(f"Connecting to SQL Server at {host}:{port}/{database} …")
    conn = pymssql.connect(host=host, port=port, user=user, password=password,
                           database=database)
    cursor = conn.cursor()

    cursor.execute("""
        SELECT c.Id, c.Subject, c.Description, c.CategoryId, c.Priority,
               c.CustomerId, c.CreatedAtUtc, c.LastContactUtc,
               cust.Name AS CustomerName
        FROM Cases c
        JOIN Customers cust ON cust.Id = c.CustomerId
        ORDER BY c.CustomerId, c.CreatedAtUtc
    """)
    rows = cursor.fetchall()
    conn.close()

    if not rows:
        print("No cases found in SQL Server. Seed some data first.")
        return 0

    column_names = ["Id", "Subject", "Description", "CategoryId", "Priority",
                    "CustomerId", "CreatedAtUtc", "LastContactUtc", "CustomerName"]

    prior_counts: dict[int, int] = {}
    output_rows = []

    for row in rows:
        r = dict(zip(column_names, row))
        customer_id = r["CustomerId"]
        prior_case_count = prior_counts.get(customer_id, 0)
        prior_counts[customer_id] = prior_case_count + 1

        now = datetime.now(timezone.utc)
        last_contact = parse_utc(str(r["LastContactUtc"])) if r["LastContactUtc"] else None
        if last_contact is None:
            created = parse_utc(str(r["CreatedAtUtc"])) if r["CreatedAtUtc"] else None
            days_since = max(0, (now - created).days) if created else 0
        else:
            days_since = max(0, (now - last_contact).days)

        text = f"{r['Subject'] or ''} {r['Description'] or ''}"
        sentiment = sentiment_score(text)
        priority_label = PRIORITY_MAP.get(r["Priority"], "Medium")

        output_rows.append({
            "category_id": r["CategoryId"],
            "prior_case_count": prior_case_count,
            "days_since_contact": days_since,
            "sentiment": round(sentiment, 4),
            "priority": priority_label,
        })

    out_path = Path(output)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    with open(out_path, "w", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=[
            "category_id", "prior_case_count", "days_since_contact",
            "sentiment", "priority",
        ])
        writer.writeheader()
        writer.writerows(output_rows)

    print(f"Exported {len(output_rows)} training rows → {out_path}")
    for label in LABELS:
        count = sum(1 for r in output_rows if r["priority"] == label)
        print(f"    {label}: {count} ({count / len(output_rows) * 100:.1f}%)")
    return len(output_rows)


def main() -> None:
    parser = argparse.ArgumentParser(description="Export training data from the database.")
    parser.add_argument("--db", default="customer_service.db",
                        help="Path to SQLite database file (default: customer_service.db)")
    parser.add_argument("--mssql", action="store_true",
                        help="Export from SQL Server instead of SQLite")
    parser.add_argument("-o", "--output", default="ml/data/training_data.csv",
                        help="Output CSV path (default: ml/data/training_data.csv)")
    args = parser.parse_args()

    if args.mssql:
        count = export_mssql(args.output)
    else:
        count = export_sqlite(args.db, args.output)

    if count == 0:
        print("\nNo data exported. To generate training data from the app:")
        print("  1. Set Database:Provider to Sqlite in appsettings.json")
        print("  2. Run the backend once to seed the database")
        print("  3. Create some cases through the UI")
        print("  4. Run this script again")
    else:
        print(f"\nReady. Now run: python ml/train_model.py --data {args.output}")


if __name__ == "__main__":
    main()

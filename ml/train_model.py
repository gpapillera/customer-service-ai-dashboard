"""ML training pipeline for the Customer Service AI Dashboard.

Generates a synthetic, rule-labeled dataset of support cases, trains a small
multiclass classifier (Decision Tree / Random Forest) that predicts case
``Priority`` (Low / Medium / High), evaluates it, and exports the model to ONNX
(``ml/models/priority_model.onnx``) for inference in the ASP.NET Core backend.

The exported ONNX model expects a 4-feature float input named ``input`` with
the exact ordering consumed by ``CustomerService.ML.OnnxPriorityPredictor``:

    [categoryId, priorCaseCount, daysSinceLastContact, hasComplaintKeyword]

and returns a 3-element probability vector in the order [Low, Medium, High].

Because there is no real historical dataset, the training data is synthetic and
rule-labeled (see ``label_rule``). This is documented as a limitation in
``docs/MODEL_CARD.md``.
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
import pandas as pd
from sklearn.metrics import accuracy_score, classification_report, confusion_matrix
from sklearn.model_selection import train_test_split
from sklearn.tree import DecisionTreeClassifier
from skl2onnx import convert_sklearn
from skl2onnx.common.data_types import FloatTensorType

# Canonical category vocabulary aligned with the backend seed categories.
# The integer id is what the backend passes as the first model feature.
CATEGORIES = {
    "Billing": 1,
    "Shipping / Supply Chain": 2,
    "Technical Support": 3,
    "Account": 4,
    "Product Quality": 5,
    "General Inquiry": 6,
    "Uncategorized": 7,
}

# Urgency / complaint keywords used both for labeling and feature extraction.
COMPLAINT_KEYWORDS = [
    "urgent", "asap", "immediately", "broken", "error", "fail", "failed",
    "complaint", "angry", "furious", "unacceptable", "refund", "chargeback",
    "lawsuit", "escalate", "critical", "down", "outage", "lost", "missing",
]

LABELS = ["Low", "Medium", "High"]
LABEL_INDEX = {label: i for i, label in enumerate(LABELS)}

RANDOM_SEED = 42


def has_complaint_keyword(text: str) -> bool:
    """Return True if ``text`` contains any complaint/urgency keyword.

    Args:
        text: Free-text description or subject.

    Returns:
        True when a keyword is present (case-insensitive).
    """
    low = (text or "").lower()
    return any(kw in low for kw in COMPLAINT_KEYWORDS)


def label_rule(category_id: int, prior_case_count: int,
               days_since_contact: int, has_keyword: bool) -> str:
    """Assign a priority label from a transparent rule (the "ground truth").

    Mirrors the rule used by the backend's ``RuleBasedPriorityPredictor`` so the
    trained model approximates the same logic but generalizes from features.

    Args:
        category_id: Encoded category id.
        prior_case_count: Number of prior cases from this customer.
        days_since_contact: Days since the customer's last contact.
        has_keyword: Whether the description contains a complaint keyword.

    Returns:
        One of "Low", "Medium", "High".
    """
    score = 0
    if has_keyword:
        score += 2
    if days_since_contact > 30:
        score += 1
    if prior_case_count >= 3:
        score += 1
    if category_id == 1:  # Billing often urgent
        score += 1
    if score >= 3:
        return "High"
    if score >= 1:
        return "Medium"
    return "Low"


def generate_synthetic_data(n: int = 3000, seed: int = RANDOM_SEED) -> pd.DataFrame:
    """Generate a synthetic, rule-labeled dataset of support cases.

    Adds randomized noise so the model must learn a pattern rather than memorize
    the rule: ~8% of rows get a randomly flipped label.

    Args:
        n: Number of synthetic rows to generate.
        seed: RNG seed for reproducibility.

    Returns:
        DataFrame with columns: category_id, prior_case_count,
        days_since_contact, has_keyword (0/1), priority (label).
    """
    rng = np.random.default_rng(seed)
    cat_ids = np.array(list(CATEGORIES.values()))
    rows = []
    for _ in range(n):
        category_id = int(rng.choice(cat_ids))
        prior_case_count = int(rng.integers(0, 8))
        days_since_contact = int(rng.integers(0, 120))
        has_keyword = bool(rng.random() < 0.35)
        label = label_rule(category_id, prior_case_count, days_since_contact, has_keyword)
        rows.append(
            (category_id, prior_case_count, days_since_contact, int(has_keyword), label)
        )
    df = pd.DataFrame(
        rows,
        columns=["category_id", "prior_case_count", "days_since_contact",
                 "has_keyword", "priority"],
    )
    # Inject label noise so the classifier learns a soft boundary.
    flip_mask = rng.random(n) < 0.08
    df.loc[flip_mask, "priority"] = rng.choice(LABELS, size=int(flip_mask.sum()))
    return df


def train(df: pd.DataFrame) -> DecisionTreeClassifier:
    """Train a Decision Tree classifier on the synthetic dataset.

    Args:
        df: DataFrame produced by ``generate_synthetic_data``.

    Returns:
        A fitted DecisionTreeClassifier.
    """
    X = df[["category_id", "prior_case_count", "days_since_contact", "has_keyword"]].to_numpy(
        dtype=np.float32
    )
    # Integer labels 0=Low, 1=Medium, 2=High so the exported ONNX probability
    # vector is already in the [Low, Medium, High] order the backend expects.
    y = df["priority"].map(LABEL_INDEX).to_numpy()
    X_train, X_test, y_train, y_test = train_test_split(
        X, y, test_size=0.2, random_state=RANDOM_SEED, stratify=y
    )
    model = DecisionTreeClassifier(
        max_depth=6, min_samples_leaf=20, random_state=RANDOM_SEED
    )
    model.fit(X_train, y_train)

    # Evaluation (logged to stdout; persisted to MODEL_CARD.md by the caller).
    y_pred = model.predict(X_test)
    acc = accuracy_score(y_test, y_pred)
    print(f"Test accuracy: {acc:.3f}")
    print("\nClassification report:")
    print(classification_report(y_test, y_pred, target_names=LABELS, zero_division=0))
    print("Confusion matrix (rows=true Low/Medium/High, cols=pred):")
    print(confusion_matrix(y_test, y_pred, labels=[0, 1, 2]))
    return model


def export_onnx(model: DecisionTreeClassifier, path: Path) -> None:
    """Export a trained classifier to ONNX for backend inference.

    Args:
        model: Fitted scikit-learn classifier.
        path: Destination ``.onnx`` path (parent dirs created as needed).
    """
    path.parent.mkdir(parents=True, exist_ok=True)
    initial_type = [("input", FloatTensorType([1, 4]))]
    onnx_model = convert_sklearn(
        model,
        initial_types=initial_type,
        target_opset=17,
        options={type(model): {"zipmap": False}},
    )
    path.write_bytes(onnx_model.SerializeToString())
    print(f"Exported ONNX model -> {path}")


def main() -> None:
    """CLI entry point: generate data, train, evaluate, export ONNX."""
    parser = argparse.ArgumentParser(description="Train the priority-prediction model.")
    parser.add_argument("--rows", type=int, default=3000, help="Synthetic rows to generate.")
    parser.add_argument(
        "--output",
        default="ml/models/priority_model.onnx",
        help="Path to the exported ONNX model.",
    )
    args = parser.parse_args()

    df = generate_synthetic_data(n=args.rows)
    model = train(df)
    export_onnx(model, Path(args.output))


if __name__ == "__main__":
    main()

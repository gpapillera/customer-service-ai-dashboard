# Model Card — Case Priority Predictor

**Model:** `priority_model.onnx` (Decision Tree classifier)
**Task:** Multiclass classification — predict a support-case `Priority` of `Low`, `Medium`, or `High`.
**Owner:** Customer Service AI Dashboard (portfolio project)
**Last trained:** 2026-08-29 (regenerated synthetic baseline; prior seed-DB retrain rejected)

---

## 1. Intended Use

The model provides an **AI-suggested starting priority** for a new support case.
Human agents see the suggestion and are free to override it. It is a decision-support
tool, not an automated triage authority.

- **In scope:** New case creation in the ASP.NET Core backend (`POST /api/cases`),
  where the predicted priority is stored in `PredictedPriority` and surfaced to the agent.
- **Out of scope:** Auto-closing cases, SLA enforcement, or any action taken without
  human confirmation.

---

## 2. Model Details

| Property | Value |
|---|---|
| Algorithm | `sklearn.tree.DecisionTreeClassifier` (max_depth=6, min_samples_leaf=20) |
| Framework (train) | scikit-learn 1.5.1 (Python 3.12) |
| Export format | ONNX (opset 17) via `skl2onnx` |
| Inference (serve) | `Microsoft.ML.OnnxRuntime` 1.18.1 in C# |
| Classes | `Low` (0), `Medium` (1), `High` (2) |
| Input name | `input` (float[1,4]) |
| Output name | `probabilities` (float[1,3], order Low/Medium/High) |

### Input features (order matters — must match the backend)

| # | Feature | Type | Notes |
|---|---|---|---|
| 0 | `categoryId` | float | Encoded category id (1=Billing … 7=Uncategorized) |
| 1 | `priorCaseCount` | float | Number of prior cases from this customer |
| 2 | `daysSinceLastContact` | float | Days since the customer's last contact |
| 3 | `sentiment` | float | Sentiment score in [-1, 1] derived from the description (negative = complaint/urgency, positive = satisfaction) |

The score is computed by a lexicon-based analyzer (`sentiment_score` in
`ml/train_model.py`, mirrored by `RuleBasedPriorityPredictor.SentimentScore` in
C#). It replaces the old binary `hasComplaintKeyword` flag so the model sees a
continuous urgency signal instead of a 0/1 switch.

---

## 3. Training Data

**Shipped model (v1, current):** Synthetic data (3,000 rows) generated programmatically
in `ml/train_model.py` (`generate_synthetic_data`) and rule-labeled. This is the model
exported to `ml/models/priority_model.onnx` today — it is what the backend loads at
startup and what the API uses for `POST /api/cases` suggestions.

**Attempted retrain (v2, REJECTED):** On 2026-07-23 (Phase 23q) the model was
retrained on 15 cases exported from the seeded demo SQLite database via
`ml/export_training_data.py`. Those cases are **synthetic seed data**, not
human-triaged, so they carry no real priority signal. The resulting model scored
**33%** accuracy (it collapsed to always predicting `Medium`) — strictly *worse* than
the synthetic baseline — and was therefore **rejected** (the on-disk `.onnx` was
regenerated from synthetic data on 2026-08-29). The v2 artifacts are retained in
`ml/data/training_data.csv` only as a record of the experiment.

### Labeling
- Synthetic: assigned by the transparent `label_rule` in `train_model.py` (mirrors the
  backend's `RuleBasedPriorityPredictor` so the model approximates the same logic).
- Real-data path: each case labeled by its human-assigned `Priority` field
  (`Low`/`Medium`/`High`) — the true ground truth, **when real triaged data exists**.

### Known limitations
- **No real historical data exists in this project.** The seeded demo database is
  synthetic, so there is currently nothing genuine to retrain on. The synthetic model
  is the correct choice until real, human-reviewed case exports are available.
- **Dataset imbalance (synthetic).** Generation skews Medium; see §4.
- **Category encoding is positional.** Integer ids assume the backend seed category
  list; reuse the same encoding when retraining on a different database.
- **Sentiment heuristic is English-only and shallow.** Misses sarcasm, politeness
  masking urgency, or non-English text.

---

## 4. Evaluation

Evaluated on a 20% held-out test split (stratified), after training on the
remaining 80%. Metrics below are for the **shipped synthetic baseline (v1)**.

| Metric | Value |
|---|---|
| Test accuracy | **0.95** |
| Macro avg F1 | 0.89 |

**Classification report (test set):**

| Class | Precision | Recall | F1 | Support |
|---|---|---|---|---|
| Low | 0.91 | 0.62 | 0.73 | 47 |
| Medium | 0.95 | 0.97 | 0.96 | 340 |
| High | 0.95 | 0.98 | 0.96 | 213 |

**Confusion matrix** (rows = true, cols = predicted; order Low/Medium/High):

```
[[ 29  14   4]
 [  1 331   8]
 [  2   3 208]]
```

The synthetic generator skews Medium (the majority class), so Low recall (0.62) is
lower than the others — acceptable for a suggestion tool an agent overrides.

> **Rejected experiment (v2, seed-DB retrain):** training on the 15 synthetic seed
> rows scored **0.333** accuracy / 0.17 macro-F1 (always predicted Medium). Kept only
> as a cautionary record — do **not** ship a retrain on the seeded demo database; it
> is strictly worse than this baseline. Retrain on genuine human-triaged exports only.

---

## 5. How to Reproduce / Retrain

```bash
# from repo root, using the project venv
python3 -m venv ml/.venv && ml/.venv/bin/python -m pip install -r ml/requirements.txt
ml/.venv/bin/python ml/train_model.py --rows 3000 --output ml/models/priority_model.onnx
```

The backend loads `ml/models/priority_model.onnx` at startup. If the file is
absent, it transparently falls back to the deterministic
`RuleBasedPriorityPredictor` (same logic, no ML dependency), so the app always
runs.

### Retraining on real data

```bash
# 1. Seed the database (run the backend once with Database:Provider=Sqlite)
cd backend && dotnet run --project src/CustomerService.Api/CustomerService.Api.csproj

# 2. Export training data from the SQLite database
cd .. && source ml/.venv/bin/activate
python3 ml/export_training_data.py --db backend/src/CustomerService.Api/customer_service.db -o ml/data/training_data.csv

# 3. Retrain the model
python3 ml/train_model.py --data ml/data/training_data.csv --output ml/models/priority_model.onnx

# 4. Restart the API to pick up the new .onnx
```

> **Note:** The export script (`export_training_data.py`) extracts the same
> 4 features (categoryId, priorCaseCount, daysSinceContact, sentiment) that the
> ONNX model expects. Keep the **feature order and encoding** unchanged so the
> backend's `OnnxPriorityPredictor` keeps working.

---

## 6. Ethical & Operational Considerations

- The model is a **suggestion**, always overridable by a human agent.
- No protected-attribute features (name, gender, location) are used; the only
  customer signal is prior-case volume and recency, which are operational, not
  demographic.
- Because training data is synthetic, do **not** use this model for production
  triage without retraining on real, reviewed historical cases.

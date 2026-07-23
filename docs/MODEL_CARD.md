# Model Card — Case Priority Predictor

**Model:** `priority_model.onnx` (Decision Tree classifier)
**Task:** Multiclass classification — predict a support-case `Priority` of `Low`, `Medium`, or `High`.
**Owner:** Customer Service AI Dashboard (portfolio project)
**Last trained:** 2026-07-23 (retrained on real exported data)

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

**First generation:** Synthetic data (3,000 rows) generated programmatically in
`ml/train_model.py` (`generate_synthetic_data`). See previous revision for the
labeling rule and noise injection details.

**Current model (v2):** Retrained on **15 real cases** exported from the
application's SQLite database via `ml/export_training_data.py`.

### Labeling

Each case is labeled by its human-assigned `Priority` field (`Low`, `Medium`, or
`High`) set by the agent during case triage — this is the true ground truth.

### Features exported

| # | Feature | Source |
|---|---|---|
| 0 | `categoryId` | `Case.CategoryId` (1–7) |
| 1 | `priorCaseCount` | COUNT of earlier cases from same customer |
| 2 | `daysSinceContact` | Days since `LastContactUtc` (or `CreatedAtUtc`) |
| 3 | `sentiment` | Lexicon score in [-1,1] computed from `Subject` + `Description` |

### Known limitations

- **Very small dataset (15 rows).** The model's accuracy is low (33%) because
  there are too few samples to learn meaningful patterns. Accuracy will improve
  as more cases are triaged and the export grows.
- **Dataset imbalance.** The exported data is evenly split 20/40/40 among
  Low/Medium/High, but with only 3 Low samples the model defaults to predicting
  Medium.
- **Category encoding is positional.** The integer ids assume the backend's seed
  category list. Retraining on a different database must reuse the same encoding.
- **Sentiment heuristic is English-only and shallow.** It will miss sarcasm,
  politeness masking urgency, or non-English text.

---

## 4. Evaluation

Evaluated on a 20% held-out test split (stratified), after training on the
remaining 80%. Metrics below are for **v2 (real data, 15 rows).**

| Metric | Value |
|---|---|
| Test accuracy | **0.333** |
| Macro avg F1 | 0.17 |

**Classification report (test set):**

| Class | Precision | Recall | F1 | Support |
|---|---|---|---|---|
| Low | 0.00 | 0.00 | 0.00 | 1 |
| Medium | 0.33 | 1.00 | 0.50 | 1 |
| High | 0.00 | 0.00 | 0.00 | 1 |

**Confusion matrix** (rows = true, cols = predicted; order Low/Medium/High):

```
[[0 1 0]
 [0 1 0]
 [0 1 0]]
```

The model defaults to predicting `Medium` for everything because the training
set is too small (15 rows) to learn meaningful decision boundaries. As the
database accumulates more triaged cases, re-running the export → retrain
pipeline will improve accuracy.

> Compare with **v1 (synthetic, 3,000 rows):** accuracy was 0.93, macro avg F1
> was 0.88. The synthetic model was a placeholder until real data became
> available.

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

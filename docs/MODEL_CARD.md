# Model Card — Case Priority Predictor

**Model:** `priority_model.onnx` (Decision Tree classifier)
**Task:** Multiclass classification — predict a support-case `Priority` of `Low`, `Medium`, or `High`.
**Owner:** Customer Service AI Dashboard (portfolio project)
**Last trained:** 2026-07-11

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
| 3 | `hasComplaintKeyword` | float | 1.0 if description contains an urgency keyword, else 0.0 |

The keyword list mirrors `RuleBasedPriorityPredictor.ComplaintKeywords`
(urgent, asap, broken, refund, escalate, critical, outage, etc.).

---

## 3. Training Data

**The training data is synthetic.** There is no real historical case dataset for
this portfolio project, so data was generated programmatically in
`ml/train_model.py` (`generate_synthetic_data`, default 3,000 rows).

### Labeling rule (the "ground truth")

Each synthetic row is labeled by a transparent rule that approximates how a
support lead would triage:

```
score = 0
score += 2 if description has a complaint/urgency keyword
score += 1 if daysSinceLastContact > 30
score += 1 if priorCaseCount >= 3
score += 1 if categoryId == 1 (Billing)
priority = High if score >= 3 else Medium if score >= 1 else Low
```

To force the model to **learn a pattern** rather than memorize the rule, ~8% of
labels are randomly flipped (label noise), and feature values are drawn from broad
random ranges.

### Known limitations

- **Not representative of real traffic.** Real cases have correlations, seasonality,
  and free-text nuance this synthetic set cannot capture.
- **Category encoding is positional.** The integer ids assume the backend's seed
  category list. Retraining on real data must reuse the same encoding.
- **Keyword heuristic is English-only and shallow.** It will miss sarcasm,
  politeness masking urgency, or non-English text.

---

## 4. Evaluation

Evaluated on a 20% held-out test split (stratified), after training on the
remaining 80%.

| Metric | Value |
|---|---|
| Test accuracy | **0.93** |
| Macro avg F1 | 0.88 |

**Classification report (test set):**

| Class | Precision | Recall | F1 | Support |
|---|---|---|---|---|
| Low | 0.88 | 0.65 | 0.75 | 46 |
| Medium | 0.93 | 0.98 | 0.96 | 330 |
| High | 0.94 | 0.92 | 0.93 | 224 |

**Confusion matrix** (rows = true, cols = predicted; order Low/Medium/High):

```
[[ 30   7   9]
 [  3 324   3]
 [  1  17 206]]
```

**Rule agreement:** the exported ONNX model agrees with the labeling rule on
83/84 (98.8%) of a systematic feature grid, confirming it learned the intended
logic rather than the injected noise.

> Note: `Low` recall is lower because the synthetic `Low` class is rare (≈8% of
> rows) and easily confused with `Medium` when only one weak signal is present.
> This is acceptable for a suggestion aid; agents override as needed.

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

1. Replace `generate_synthetic_data` with a loader that reads historical cases
   (category id, prior case count, days since last contact, keyword flag → label).
2. Keep the **same feature order and encoding** so the backend's
   `OnnxPriorityPredictor` keeps working.
3. Re-run `train_model.py`; the new `.onnx` is picked up on next API start.

---

## 6. Ethical & Operational Considerations

- The model is a **suggestion**, always overridable by a human agent.
- No protected-attribute features (name, gender, location) are used; the only
  customer signal is prior-case volume and recency, which are operational, not
  demographic.
- Because training data is synthetic, do **not** use this model for production
  triage without retraining on real, reviewed historical cases.

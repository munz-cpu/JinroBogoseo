"""Compute observed study metrics from a SQLite snapshot."""
import argparse
import json
import sqlite3
from collections import defaultdict
from pathlib import Path
from statistics import mean


def metrics(rows):
    completed = [row for row in rows if row["outcome"] == "cleared"]
    errors = [abs(row["actual_attempts"] - row["target_attempts"]) for row in completed]
    dropped = sum(row["outcome"] == "stopped" for row in rows)
    active = sum(row["outcome"] == "open" for row in rows)
    resolved = len(rows) - active
    return {
        "participants": len(rows),
        "cleared": len(completed),
        "stopped": dropped,
        "open": active,
        "mean_absolute_error": mean(errors) if errors else None,
        "within_one_rate": mean(error <= 1 for error in errors) if errors else None,
        "dropout_rate": dropped / resolved if resolved else None,
    }


def achievement_by_attempts(rows, field):
    buckets = defaultdict(list)
    for row in rows:
        if row["outcome"] == "cleared":
            key = (row["group"], row["target_attempts"], row["assignment_version"], row["settings_version"], row[field])
            buckets[key].append(row["rating"])

    summary = []
    for (group, target, assignment_version, settings_version, attempts), ratings in sorted(buckets.items()):
        answered = [rating for rating in ratings if rating is not None]
        summary.append({
            "group": group, "target_attempts": target,
            "assignment_version": assignment_version, "settings_version": settings_version,
            "attempts": attempts, "cleared": len(ratings), "rated": len(answered),
            "mean_achievement_score": mean(answered) if answered else None,
        })
    return summary


def analyze(db):
    rows = []
    by_group = defaultdict(list)
    for group, target, assignment_version, settings_version, attempts, survey, latest in db.execute("""
        SELECT experiment_group, target_attempts, assignment_version, settings_version,
            (SELECT COUNT(*) FROM attempts a WHERE a.participant_id=p.participant_id),
            (SELECT payload FROM surveys s WHERE s.participant_id=p.participant_id),
            (SELECT payload FROM attempts a WHERE a.participant_id=p.participant_id
             ORDER BY attempt_number DESC LIMIT 1)
        FROM participants p
    """):
        outcome = json.loads(latest)["result"] if latest else "open"
        if outcome != "cleared":
            outcome = "stopped" if survey or outcome == "stopped" else "open"
        response = json.loads(survey) if survey and outcome == "cleared" else None
        rating = None
        if response and response["cleared"] and not response["skipped"] and len(response["scores"]) == 1:
            rating = response["scores"][0]
        row = {
            "outcome": outcome, "actual_attempts": attempts, "target_attempts": target, "group": group,
            "assignment_version": assignment_version, "settings_version": settings_version, "rating": rating,
        }
        rows.append(row)
        by_group[(group, target)].append(row)
    return {
        "overall": metrics(rows),
        "groups": [
            {"group": group, "target_attempts": target, **metrics(group_rows)}
            for (group, target), group_rows in sorted(by_group.items())
        ],
        "achievement_by_target_attempts": achievement_by_attempts(rows, "target_attempts"),
        "achievement_by_actual_attempts": achievement_by_attempts(rows, "actual_attempts"),
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("database", type=Path)
    args = parser.parse_args()
    with sqlite3.connect(args.database.resolve().as_uri() + "?mode=ro", uri=True) as db:
        print(json.dumps(analyze(db), indent=2))

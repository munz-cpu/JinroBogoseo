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


def analyze(db):
    rows = []
    by_group = defaultdict(list)
    for group, target, attempts, survey, latest in db.execute("""
        SELECT experiment_group, target_attempts,
            (SELECT COUNT(*) FROM attempts a WHERE a.participant_id=p.participant_id),
            (SELECT payload FROM surveys s WHERE s.participant_id=p.participant_id),
            (SELECT payload FROM attempts a WHERE a.participant_id=p.participant_id
             ORDER BY attempt_number DESC LIMIT 1)
        FROM participants p
    """):
        outcome = json.loads(latest)["result"] if latest else "open"
        if outcome != "cleared":
            outcome = "stopped" if survey or outcome == "stopped" else "open"
        row = {"outcome": outcome, "actual_attempts": attempts, "target_attempts": target}
        rows.append(row)
        by_group[(group, target)].append(row)
    return {
        "overall": metrics(rows),
        "groups": [
            {"group": group, "target_attempts": target, **metrics(group_rows)}
            for (group, target), group_rows in sorted(by_group.items())
        ],
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("database", type=Path)
    args = parser.parse_args()
    with sqlite3.connect(args.database.resolve().as_uri() + "?mode=ro", uri=True) as db:
        print(json.dumps(analyze(db), indent=2))

from datetime import datetime
from typing import Annotated, Literal
from uuid import UUID

from pydantic import AwareDatetime, BaseModel, ConfigDict, Field, StringConstraints, model_validator

Label = Annotated[str, StringConstraints(strip_whitespace=True, min_length=1, max_length=80)]
Count = Annotated[int, Field(ge=0, le=100_000)]
PositiveCount = Annotated[int, Field(ge=1, le=100_000)]
Amount = Annotated[float, Field(ge=0, le=1_000_000)]
Ratio = Annotated[float, Field(ge=0, le=1)]
Factor = Annotated[float, Field(ge=0.1, le=5)]


class Payload(BaseModel):
    model_config = ConfigDict(extra="forbid", allow_inf_nan=False)


class Group(Payload):
    group: Label
    target_attempts: PositiveCount
    weight: Annotated[float, Field(gt=0, le=1_000_000)]


class Participant(Payload):
    participant_id: UUID
    age_group: Literal["under_18", "18_24", "25_34", "35_44", "45_plus", "undisclosed"]
    gaming_experience: Literal["rarely", "monthly", "weekly", "daily", "undisclosed"]
    consented: Literal[True]
    consented_at: AwareDatetime
    consent_version: Label
    settings_version: Label


class Assignment(Payload):
    experiment_group: Label
    target_attempts: PositiveCount
    assignment_version: Label


class PatternParameters(Payload):
    pattern_id: Label
    speed: Factor
    interval: Factor
    hitbox: Factor
    damage: Factor
    selection_weight: Factor


class Difficulty(Payload):
    level: Ratio
    boss_health: Factor
    patterns: list[PatternParameters] = Field(min_length=1, max_length=64)

    @model_validator(mode="after")
    def unique_patterns(self):
        if len({p.pattern_id for p in self.patterns}) != len(self.patterns):
            raise ValueError("duplicate difficulty pattern")
        return self


class DifficultyChange(Payload):
    applied_at: AwareDatetime
    previous_level: Ratio
    new_level: Ratio
    reason: Literal["initial", "ahead_of_target", "near_target", "struggling", "hold"]


class PatternStat(Payload):
    pattern_id: Label
    executions: Count
    hit_count: Count
    average_damage: Amount
    damage_taken: Amount


class Attempt(Assignment):
    participant_id: UUID
    attempt_id: UUID
    attempt_number: PositiveCount
    settings_version: Label
    started_at: AwareDatetime
    ended_at: AwareDatetime
    duration_seconds: Amount
    damage_taken: Amount
    damage_dealt: Amount
    boss_hp_remaining: Ratio
    pattern_stats: list[PatternStat] = Field(min_length=1, max_length=64)
    difficulty: Difficulty
    difficulty_changes: list[DifficultyChange] = Field(min_length=1, max_length=1)
    cleared: bool
    result: Literal["cleared", "defeat", "stopped", "interrupted"]

    @model_validator(mode="after")
    def consistent_attempt(self):
        if self.ended_at < self.started_at or self.cleared != (self.result == "cleared"):
            raise ValueError("inconsistent attempt outcome or time")
        expected = {p.pattern_id for p in self.difficulty.patterns}
        if len(self.pattern_stats) != len(expected) or {p.pattern_id for p in self.pattern_stats} != expected:
            raise ValueError("pattern stats must match difficulty patterns")
        change = self.difficulty_changes[0]
        if change.new_level != self.difficulty.level or change.applied_at != self.started_at:
            raise ValueError("difficulty change must match the attempt start")
        return self


class Survey(Payload):
    participant_id: UUID
    settings_version: Label
    submitted_at: AwareDatetime
    cleared: bool
    actual_attempts: Count
    skipped: bool
    scores: list[Annotated[int, Field(strict=True, ge=1, le=5)]] = Field(max_length=6)

    @model_validator(mode="after")
    def valid_scores(self):
        if len(self.scores) not in ((0,) if self.skipped else (1, 6)):
            raise ValueError("expected one rating, six legacy scores, or an empty skipped response")
        return self


class Withdrawal(Payload):
    participant_id: UUID

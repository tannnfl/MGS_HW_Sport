# MGS Sport — Game Design Document

## Overview

A multiplayer dodgeball game for 2–10 players. Two players are randomly assigned as **Throwers**; all others are **Dodgers**. Throwers try to eliminate Dodgers with the ball. The game runs for multiple rounds, accumulating scores, and ends with a leaderboard.

---

## Players & Roles

| Role | Count | Description |
|------|-------|-------------|
| Thrower | 2 (random) | Pick up and throw the ball to eliminate Dodgers |
| Dodger | All others | Survive as long as possible to earn score |

- Roles are assigned **randomly** at the start of each round.
- Each role has its own spawn zone; players must stay within their area.

---

## Session Start

- The **host** sees a **Start** button.
- The Start button is only enabled when **at least 4 players** are connected.
- Once started, roles are randomly assigned and the round begins.

---

## Round Gameplay

### Ball Mechanics
- The ball starts free (unowned).
- Any **Thrower** can pick up the ball when it is free (no one is holding it).
- A Thrower **holding the ball** can throw it toward the Dodger area.
- If the thrown ball hits a **Dodger**, that Dodger is eliminated.

### Elimination
- When a Dodger is hit by the ball:
  1. They are marked **out** for this round.
  2. They are **teleported to the Thrower spawn area** and join the Thrower team.
  3. They can now also pick up and throw the ball.
- Eliminated Dodgers do **not** receive any win-condition score for that round.

---

## Timer

```
Round Timer = 30 + (15 × player_count)  seconds
```

Example: 6 players → 30 + 90 = **120 seconds**

---

## Win Conditions & Scoring

There are three possible outcomes per round, checked in order:

---

### Condition 1 — Throwers Win Early (before 50% of timer elapsed)

**Trigger:** All Dodgers are eliminated **before** half the round timer has passed.

**Score for each Thrower (including converted ex-Dodgers now on Thrower side):**
```
score += player_count + hit_count + (0.1 × time_remaining)
```
- `player_count` = total players in this round
- `hit_count` = total number of Dodgers eliminated this round
- `time_remaining` = seconds left on the timer when last Dodger was eliminated

---

### Condition 2 — Last Dodger Wins (before timer reaches 0)

**Trigger:** Exactly **1 Dodger remains** at any point before the timer expires.

**Score for that surviving Dodger:**
```
score += 3 × player_count + time_remaining
```
- `time_remaining` = seconds left on the timer when they are declared the winner

---

### Condition 3 — Dodgers Survive the Timer

**Trigger:** The timer reaches **0** with more than 1 Dodger still alive.

**Score for each surviving Dodger:**
```
score += 2 × (player_count - survivor_count)
```
- `survivor_count` = number of Dodgers still alive when the timer hit zero

---

## Score Display

- All players' scores are **always visible on screen** during play (live leaderboard).
- Scores persist and accumulate across rounds.

---

## Player Names

- Each player can **type their own name** at any time; it updates live on everyone's leaderboard.
- Default name if none entered: `Player#[clientId + 1]`
  - Example: client ID 0 → `Player#1`, client ID 3 → `Player#4`

---

## Round Transition

After each round ends:
1. The **2 Dodgers who lasted the longest** this round become the **Throwers** in the next round.
   - "Longest lasting" = eliminated last (by order of hit time), or survived.
   - If the last Dodger won (Condition 2), they count as one of the two.
   - If there are **multiple survivors** (Condition 3), one of the survivors is chosen **randomly** to fill any remaining Thrower slot alongside the latest-eliminated Dodger.
2. All other role/position assignments reset.
3. A new round begins.

---

## Game End

```
Game ends after:  floor(player_count / 2)  rounds
```

Example: 6 players → `floor(6 / 2)` = **3 rounds**

At game end:
- A **final leaderboard** is shown with all players ranked by total accumulated score.
- Players can still update their name here and it reflects on everyone's screen.

---

## Summary Table

| Event | Who scores | Formula |
|-------|-----------|---------|
| All Dodgers out before 50% timer | Each Thrower (incl. converted) | `player_count + hit_count + 0.1 × time_left` |
| 1 Dodger survives (before timer 0) | That Dodger | `3 × player_count + time_left` |
| Timer hits 0 (2+ survivors) | Each surviving Dodger | `2 × (player_count − survivor_count)` |

---

## Round Count Examples

| Players | Timer | Rounds |
|---------|-------|--------|
| 4 | 90s | 2 |
| 6 | 120s | 3 |
| 8 | 150s | 4 |
| 10 | 180s | 5 |

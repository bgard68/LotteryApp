# Lottery.Domain

The innermost layer: pure business logic with **zero dependencies** - no
packages, no framework references, no clock, no I/O. Everything here is a pure
function or immutable value, which is why the whole layer unit-tests without
mocks.

Back to the [main README](../../README.md).

| File | Responsibility |
|---|---|
| `Game.cs` | The two games + their draw days/times (ET) as data |
| `Draw.cs` | A published drawing; whites stored sorted; value equality by content |
| `DrawStatus.cs` | `Scheduled` / `Pending` / `Published` - a drawing that happened but has no numbers yet is *Pending*, never silently hidden |
| `DrawSchedule.cs` | Next/previous draw math. ET wall-clock is built first, then converted to UTC via the `America/New_York` tz database, so DST is the timezone's problem. Callers pass "now" in - this type never reads a clock |
| `RuleEra.cs` / `RuleEras.cs` | Number-matrix eras (7 Powerball back to 1992, 5 Mega Millions). Rule changes are **data**, validated against the entire real history by tests |
| `EraValidator.cs` | Flags any draw outside its era - the mechanism that turns an unknown future rule change into a loud failure |
| `TicketMatcher.cs` | Order-independent matching: whites via set intersection, special ball via strict equality, kept fully separate |
| `PrizeTiers.cs` | The 9 official tiers per game; names are stable across history, amounts are current-era approximations |
| `IPickGenerator.cs` | Pick generation port + `RandomPickGenerator` (partial Fisher-Yates: uniform, duplicate-free, no retry loop; seeded `Random` injectable for tests) |

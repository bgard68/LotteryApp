CREATE TABLE JackpotEstimates (
    Game TEXT NOT NULL PRIMARY KEY,
    NextEstimatedJackpot NUMERIC NULL,
    NextCashValue NUMERIC NULL,
    UpdatedAtUtc TEXT NOT NULL
);

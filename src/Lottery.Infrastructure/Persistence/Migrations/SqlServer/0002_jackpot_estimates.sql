CREATE TABLE JackpotEstimates (
    Game NVARCHAR(20) NOT NULL PRIMARY KEY,
    NextEstimatedJackpot DECIMAL(15,2) NULL,
    NextCashValue DECIMAL(15,2) NULL,
    UpdatedAtUtc DATETIMEOFFSET NOT NULL
);

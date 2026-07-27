CREATE TABLE Draws (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Game TEXT NOT NULL,
    DrawDate TEXT NOT NULL,
    White1 INTEGER NOT NULL,
    White2 INTEGER NOT NULL,
    White3 INTEGER NOT NULL,
    White4 INTEGER NOT NULL,
    White5 INTEGER NOT NULL,
    Special INTEGER NOT NULL,
    JackpotAmount NUMERIC NULL,
    JackpotWon INTEGER NULL
);

CREATE UNIQUE INDEX UX_Draws_Game_DrawDate ON Draws (Game, DrawDate);

CREATE TABLE ImportLedger (
    Game TEXT NOT NULL PRIMARY KEY,
    Source TEXT NOT NULL,
    CompletedAtUtc TEXT NOT NULL,
    DrawCount INTEGER NOT NULL,
    EarliestDraw TEXT NOT NULL,
    LatestDraw TEXT NOT NULL
);

--
-- File generated with SQLiteStudio v3.3.3 on mer. juin 22 13:53:18 2022
--
-- Text encoding used: System
--
PRAGMA foreign_keys = off;
BEGIN TRANSACTION;

-- Table: __EFMigrationsHistory
DROP TABLE IF EXISTS __EFMigrationsHistory;

CREATE TABLE __EFMigrationsHistory (
    MigrationId    TEXT NOT NULL
                        CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY,
    ProductVersion TEXT NOT NULL
);

INSERT INTO __EFMigrationsHistory (
                                      MigrationId,
                                      ProductVersion
                                  )
                                  VALUES (
                                      '20220509125849_Initial',
                                      '6.0.4'
                                  );


-- Table: Authors
DROP TABLE IF EXISTS Authors;

CREATE TABLE Authors (
    AuthorID INTEGER NOT NULL
                     CONSTRAINT PK_Authors PRIMARY KEY AUTOINCREMENT,
    Name     TEXT    NOT NULL,
    URL      TEXT    NOT NULL
);


-- Table: Mods
DROP TABLE IF EXISTS Mods;

CREATE TABLE Mods (
    ModID            INTEGER NOT NULL
                             CONSTRAINT PK_Mods PRIMARY KEY AUTOINCREMENT,
    Name             TEXT,
    Url              TEXT,
    AuthorID         INTEGER,
    Path             TEXT,
    SteamLastUpdated TEXT,
    LocalLastUpdated TEXT,
    IsLocal          INTEGER,
    ModStatus        TEXT,
    ExpectedSize     INTEGER,
    ActualSize       INTEGER,
    CONSTRAINT FK_Mods_Authors_AuthorID FOREIGN KEY (
        AuthorID
    )
    REFERENCES Authors (AuthorID) 
);

INSERT INTO Mods (
                     ModID,
                     Name,
                     Url,
                     AuthorID,
                     Path,
                     SteamLastUpdated,
                     LocalLastUpdated,
                     IsLocal,
                     ModStatus,
                     ExpectedSize,
                     ActualSize
                 )
                 VALUES (
                     463939058,
                     'TestName',
                     'TestUrl',
                     NULL,
                     NULL,
                     NULL,
                     NULL,
                     NULL,
                     NULL,
                     NULL,
                     NULL
                 );


-- Table: Settings
DROP TABLE IF EXISTS Settings;

CREATE TABLE Settings (
    ArmaPath        TEXT,
    ModStagingDir   TEXT,
    UsingContactDlc INTEGER NOT NULL,
    UsingGmDlc      INTEGER NOT NULL,
    UsingPfDlc      INTEGER NOT NULL,
    UsingClsaDlc    INTEGER NOT NULL,
    UseWsDlc        INTEGER NOT NULL,
    ApiKey          TEXT,
    CliWorkers      INTEGER
);


-- Table: Users
DROP TABLE IF EXISTS Users;

CREATE TABLE Users (
    Id    TEXT NOT NULL
               CONSTRAINT PK_Users PRIMARY KEY,
    Login TEXT NOT NULL,
    Pass  TEXT NOT NULL,
    Name  TEXT NOT NULL
);


-- Index: IX_Mods_AuthorID
DROP INDEX IF EXISTS IX_Mods_AuthorID;

CREATE INDEX IX_Mods_AuthorID ON Mods (
    "AuthorID"
);


COMMIT TRANSACTION;
PRAGMA foreign_keys = on;

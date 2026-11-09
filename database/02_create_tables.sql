-- GolfStatsApp schema (generated from AppDbContext via 'dotnet ef dbcontext script').
-- Run after 01_create_database.sql against an empty database.
USE [GolfStatsApp];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE TABLE [AspNetRoles] (
    [Id] nvarchar(450) NOT NULL,
    [Name] nvarchar(256) NULL,
    [NormalizedName] nvarchar(256) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [AspNetUsers] (
    [Id] nvarchar(450) NOT NULL,
    [DisplayName] nvarchar(max) NOT NULL,
    [UserName] nvarchar(256) NULL,
    [NormalizedUserName] nvarchar(256) NULL,
    [Email] nvarchar(256) NULL,
    [NormalizedEmail] nvarchar(256) NULL,
    [EmailConfirmed] bit NOT NULL,
    [PasswordHash] nvarchar(max) NULL,
    [SecurityStamp] nvarchar(max) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    [PhoneNumber] nvarchar(max) NULL,
    [PhoneNumberConfirmed] bit NOT NULL,
    [TwoFactorEnabled] bit NOT NULL,
    [LockoutEnd] datetimeoffset NULL,
    [LockoutEnabled] bit NOT NULL,
    [AccessFailedCount] int NOT NULL,
    CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [GolferRounds] (
    [Id] int NOT NULL IDENTITY,
    [TournamentId] nvarchar(450) NOT NULL,
    [PlayerId] nvarchar(450) NOT NULL,
    [Round] int NOT NULL,
    [Holes] int NOT NULL,
    [ToPar] int NOT NULL,
    [HolePoints] float NOT NULL,
    [BogeyFree] bit NOT NULL,
    CONSTRAINT [PK_GolferRounds] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Leagues] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NOT NULL,
    [JoinCode] nvarchar(450) NOT NULL,
    [OwnerId] nvarchar(max) NOT NULL,
    [PlayersPerTeam] int NOT NULL,
    [CreatedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_Leagues] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [PlayerPoints] (
    [Id] int NOT NULL IDENTITY,
    [PlayerName] nvarchar(max) NOT NULL,
    [Tournament] nvarchar(max) NOT NULL,
    [Points] float NOT NULL,
    CONSTRAINT [PK_PlayerPoints] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [PlayerResultSnapshots] (
    [Id] int NOT NULL IDENTITY,
    [PlayerId] nvarchar(450) NOT NULL,
    [TournamentId] nvarchar(450) NOT NULL,
    [TournamentName] nvarchar(max) NOT NULL,
    [Season] int NOT NULL,
    [EndDate] datetime2 NULL,
    [EventType] nvarchar(max) NOT NULL,
    [Position] nvarchar(max) NULL,
    [Status] nvarchar(max) NOT NULL,
    [ToPar] int NULL,
    [Rounds] int NOT NULL,
    [CapturedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_PlayerResultSnapshots] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [StatSnapshots] (
    [Id] int NOT NULL IDENTITY,
    [StatId] nvarchar(450) NOT NULL,
    [PlayerId] nvarchar(450) NOT NULL,
    [PlayerName] nvarchar(max) NOT NULL,
    [Season] int NOT NULL,
    [Value] float NOT NULL,
    [Rank] int NULL,
    [CapturedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_StatSnapshots] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [SyncedTournaments] (
    [TournamentId] nvarchar(450) NOT NULL,
    [IsFinal] bit NOT NULL,
    [CutKnown] bit NOT NULL,
    [CompleteRounds] nvarchar(max) NOT NULL,
    [StartedRound] int NOT NULL,
    [LastSyncedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_SyncedTournaments] PRIMARY KEY ([TournamentId])
);
GO


CREATE TABLE [Teams] (
    [Id] int NOT NULL IDENTITY,
    [TeamName] nvarchar(450) NOT NULL,
    [OverallAverage] float NOT NULL,
    [AverageWithRoster] float NOT NULL,
    [BestTournamentScore] int NOT NULL,
    [BestTournament] nvarchar(max) NOT NULL,
    [WorstTournamentScore] int NOT NULL,
    [WorstTournament] nvarchar(max) NOT NULL,
    [LongestRosterStreak] int NOT NULL,
    [Wins] float NOT NULL,
    [Majors] float NOT NULL,
    [SignatureEvents] float NOT NULL,
    [Regulars] float NOT NULL,
    CONSTRAINT [PK_Teams] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [TournamentGolfers] (
    [Id] int NOT NULL IDENTITY,
    [TournamentId] nvarchar(450) NOT NULL,
    [PlayerId] nvarchar(450) NOT NULL,
    [PlayerName] nvarchar(max) NOT NULL,
    [Position] nvarchar(max) NULL,
    [State] nvarchar(max) NULL,
    CONSTRAINT [PK_TournamentGolfers] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [AspNetRoleClaims] (
    [Id] int NOT NULL IDENTITY,
    [RoleId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [AspNetUserClaims] (
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [AspNetUserLogins] (
    [LoginProvider] nvarchar(450) NOT NULL,
    [ProviderKey] nvarchar(450) NOT NULL,
    [ProviderDisplayName] nvarchar(max) NULL,
    [UserId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
    CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [AspNetUserRoles] (
    [UserId] nvarchar(450) NOT NULL,
    [RoleId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
    CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [AspNetUserTokens] (
    [UserId] nvarchar(450) NOT NULL,
    [LoginProvider] nvarchar(450) NOT NULL,
    [Name] nvarchar(450) NOT NULL,
    [Value] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
    CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [LeagueMembers] (
    [Id] int NOT NULL IDENTITY,
    [LeagueId] int NOT NULL,
    [UserId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_LeagueMembers] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_LeagueMembers_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_LeagueMembers_Leagues_LeagueId] FOREIGN KEY ([LeagueId]) REFERENCES [Leagues] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [LeagueTournaments] (
    [Id] int NOT NULL IDENTITY,
    [LeagueId] int NOT NULL,
    [TournamentId] nvarchar(450) NOT NULL,
    [Name] nvarchar(max) NOT NULL,
    [Segment] int NOT NULL,
    [EventType] nvarchar(max) NOT NULL,
    [StartDate] datetime2 NULL,
    CONSTRAINT [PK_LeagueTournaments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_LeagueTournaments_Leagues_LeagueId] FOREIGN KEY ([LeagueId]) REFERENCES [Leagues] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [PlayerResultRounds] (
    [Id] int NOT NULL IDENTITY,
    [SnapshotId] int NOT NULL,
    [Round] int NOT NULL,
    [ToPar] int NOT NULL,
    CONSTRAINT [PK_PlayerResultRounds] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PlayerResultRounds_PlayerResultSnapshots_SnapshotId] FOREIGN KEY ([SnapshotId]) REFERENCES [PlayerResultSnapshots] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [TeamTournamentEntries] (
    [Id] int NOT NULL IDENTITY,
    [LeagueTournamentId] int NOT NULL,
    [MemberId] int NOT NULL,
    [CaptainPlayerId] nvarchar(max) NULL,
    [CaptainName] nvarchar(max) NULL,
    [CaptainFromRound] int NOT NULL,
    CONSTRAINT [PK_TeamTournamentEntries] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_TeamTournamentEntries_LeagueMembers_MemberId] FOREIGN KEY ([MemberId]) REFERENCES [LeagueMembers] ([Id]),
    CONSTRAINT [FK_TeamTournamentEntries_LeagueTournaments_LeagueTournamentId] FOREIGN KEY ([LeagueTournamentId]) REFERENCES [LeagueTournaments] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [LineupSlots] (
    [Id] int NOT NULL IDENTITY,
    [EntryId] int NOT NULL,
    [FromRound] int NOT NULL,
    [PlayerId] nvarchar(max) NOT NULL,
    [PlayerName] nvarchar(max) NOT NULL,
    [IsStarter] bit NOT NULL,
    [BenchPriority] int NOT NULL,
    CONSTRAINT [PK_LineupSlots] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_LineupSlots_TeamTournamentEntries_EntryId] FOREIGN KEY ([EntryId]) REFERENCES [TeamTournamentEntries] ([Id]) ON DELETE CASCADE
);
GO


CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
GO


CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;
GO


CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
GO


CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
GO


CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
GO


CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
GO


CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL;
GO


CREATE UNIQUE INDEX [IX_GolferRounds_TournamentId_PlayerId_Round] ON [GolferRounds] ([TournamentId], [PlayerId], [Round]);
GO


CREATE UNIQUE INDEX [IX_LeagueMembers_LeagueId_UserId] ON [LeagueMembers] ([LeagueId], [UserId]);
GO


CREATE INDEX [IX_LeagueMembers_UserId] ON [LeagueMembers] ([UserId]);
GO


CREATE UNIQUE INDEX [IX_Leagues_JoinCode] ON [Leagues] ([JoinCode]);
GO


CREATE UNIQUE INDEX [IX_LeagueTournaments_LeagueId_TournamentId] ON [LeagueTournaments] ([LeagueId], [TournamentId]);
GO


CREATE INDEX [IX_LineupSlots_EntryId_FromRound] ON [LineupSlots] ([EntryId], [FromRound]);
GO


CREATE UNIQUE INDEX [IX_PlayerResultRounds_SnapshotId_Round] ON [PlayerResultRounds] ([SnapshotId], [Round]);
GO


CREATE UNIQUE INDEX [IX_PlayerResultSnapshots_PlayerId_TournamentId] ON [PlayerResultSnapshots] ([PlayerId], [TournamentId]);
GO


CREATE UNIQUE INDEX [IX_StatSnapshots_StatId_PlayerId_Season] ON [StatSnapshots] ([StatId], [PlayerId], [Season]);
GO


CREATE UNIQUE INDEX [IX_Teams_TeamName] ON [Teams] ([TeamName]);
GO


CREATE UNIQUE INDEX [IX_TeamTournamentEntries_LeagueTournamentId_MemberId] ON [TeamTournamentEntries] ([LeagueTournamentId], [MemberId]);
GO


CREATE INDEX [IX_TeamTournamentEntries_MemberId] ON [TeamTournamentEntries] ([MemberId]);
GO


CREATE UNIQUE INDEX [IX_TournamentGolfers_TournamentId_PlayerId] ON [TournamentGolfers] ([TournamentId], [PlayerId]);
GO




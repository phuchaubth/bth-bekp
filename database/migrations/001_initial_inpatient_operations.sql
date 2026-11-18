IF OBJECT_ID(N'[bekp].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'bekp') IS NULL EXEC(N'CREATE SCHEMA [bekp];');
    CREATE TABLE [bekp].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    IF SCHEMA_ID(N'bekp') IS NULL EXEC(N'CREATE SCHEMA [bekp];');
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    CREATE TABLE [bekp].[BloodInventory] (
        [Id] uniqueidentifier NOT NULL,
        [Product] varchar(20) NOT NULL,
        [AboGroup] varchar(2) NOT NULL,
        [UnitsAvailable] int NOT NULL,
        [MinimumUnits] int NOT NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_BloodInventory] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    CREATE TABLE [bekp].[Departments] (
        [Id] uniqueidentifier NOT NULL,
        [Code] varchar(16) NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [Description] nvarchar(250) NOT NULL,
        [Group] varchar(40) NOT NULL,
        [IsSterileUnit] bit NOT NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_Departments] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    CREATE TABLE [bekp].[OperationalEvents] (
        [Id] uniqueidentifier NOT NULL,
        [Type] varchar(40) NOT NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        [DepartmentId] uniqueidentifier NULL,
        [DepartmentCode] varchar(16) NULL,
        [StayCode] varchar(16) NULL,
        [Subject] nvarchar(80) NULL,
        [Value] nvarchar(80) NULL,
        CONSTRAINT [PK_OperationalEvents] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    CREATE TABLE [bekp].[Beds] (
        [Id] uniqueidentifier NOT NULL,
        [DepartmentId] uniqueidentifier NOT NULL,
        [Code] varchar(16) NOT NULL,
        [RoomLabel] nvarchar(40) NOT NULL,
        [IsExtra] bit NOT NULL,
        [SortOrder] int NOT NULL,
        [Status] varchar(20) NOT NULL,
        [StatusChangedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_Beds] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Beds_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [bekp].[Departments] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    CREATE TABLE [bekp].[ProcessTimings] (
        [Id] uniqueidentifier NOT NULL,
        [Standard] varchar(40) NOT NULL,
        [DepartmentId] uniqueidentifier NOT NULL,
        [StartedAtUtc] datetime2 NOT NULL,
        [CompletedAtUtc] datetime2 NOT NULL,
        [DurationMinutes] int NOT NULL,
        [Subject] varchar(40) NULL,
        CONSTRAINT [PK_ProcessTimings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProcessTimings_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [bekp].[Departments] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    CREATE TABLE [bekp].[InpatientStays] (
        [Id] uniqueidentifier NOT NULL,
        [Code] varchar(16) NOT NULL,
        [DepartmentId] uniqueidentifier NOT NULL,
        [BedId] uniqueidentifier NOT NULL,
        [AdmittedAtUtc] datetime2 NOT NULL,
        [DischargedAtUtc] datetime2 NULL,
        [CareLevel] varchar(10) NOT NULL,
        [PrincipalDiagnosisCode] varchar(8) NOT NULL,
        [DiseaseGroup] varchar(40) NOT NULL,
        [IsNeutropenic] bit NOT NULL,
        [IsTransfusing] bit NOT NULL,
        [HasChemotherapyToday] bit NOT NULL,
        [ExpectedDischargeDate] date NULL,
        CONSTRAINT [PK_InpatientStays] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InpatientStays_Beds_BedId] FOREIGN KEY ([BedId]) REFERENCES [bekp].[Beds] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InpatientStays_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [bekp].[Departments] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    CREATE TABLE [bekp].[BloodRequests] (
        [Id] uniqueidentifier NOT NULL,
        [StayId] uniqueidentifier NOT NULL,
        [DepartmentId] uniqueidentifier NOT NULL,
        [Product] varchar(20) NOT NULL,
        [AboGroup] varchar(2) NOT NULL,
        [RhDPositive] bit NOT NULL,
        [Units] int NOT NULL,
        [Urgency] varchar(12) NOT NULL,
        [Modifiers] int NOT NULL,
        [Status] varchar(16) NOT NULL,
        [RequestedAtUtc] datetime2 NOT NULL,
        [StatusChangedAtUtc] datetime2 NOT NULL,
        [IssuedAtUtc] datetime2 NULL,
        [TransfusionStartedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_BloodRequests] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BloodRequests_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [bekp].[Departments] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_BloodRequests_InpatientStays_StayId] FOREIGN KEY ([StayId]) REFERENCES [bekp].[InpatientStays] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    CREATE TABLE [bekp].[FebrileNeutropeniaEpisodes] (
        [Id] uniqueidentifier NOT NULL,
        [StayId] uniqueidentifier NOT NULL,
        [DepartmentId] uniqueidentifier NOT NULL,
        [FeverDetectedAtUtc] datetime2 NOT NULL,
        [TemperatureCelsius] decimal(4,1) NOT NULL,
        [AntibioticStartedAtUtc] datetime2 NULL,
        CONSTRAINT [PK_FebrileNeutropeniaEpisodes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FebrileNeutropeniaEpisodes_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [bekp].[Departments] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_FebrileNeutropeniaEpisodes_InpatientStays_StayId] FOREIGN KEY ([StayId]) REFERENCES [bekp].[InpatientStays] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Beds_DepartmentId_Code] ON [bekp].[Beds] ([DepartmentId], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    CREATE UNIQUE INDEX [IX_BloodInventory_Product_AboGroup] ON [bekp].[BloodInventory] ([Product], [AboGroup]);
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    CREATE INDEX [IX_BloodRequests_DepartmentId_Status] ON [bekp].[BloodRequests] ([DepartmentId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    CREATE INDEX [IX_BloodRequests_IssuedAtUtc] ON [bekp].[BloodRequests] ([IssuedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    CREATE INDEX [IX_BloodRequests_StayId] ON [bekp].[BloodRequests] ([StayId]);
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Departments_Code] ON [bekp].[Departments] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    CREATE INDEX [IX_FebrileNeutropeniaEpisodes_DepartmentId_AntibioticStartedAtUtc] ON [bekp].[FebrileNeutropeniaEpisodes] ([DepartmentId], [AntibioticStartedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    CREATE INDEX [IX_FebrileNeutropeniaEpisodes_StayId] ON [bekp].[FebrileNeutropeniaEpisodes] ([StayId]);
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    CREATE INDEX [IX_InpatientStays_AdmittedAtUtc] ON [bekp].[InpatientStays] ([AdmittedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    CREATE INDEX [IX_InpatientStays_BedId] ON [bekp].[InpatientStays] ([BedId]);
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    CREATE UNIQUE INDEX [IX_InpatientStays_Code] ON [bekp].[InpatientStays] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    CREATE INDEX [IX_InpatientStays_DepartmentId_DischargedAtUtc] ON [bekp].[InpatientStays] ([DepartmentId], [DischargedAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    CREATE INDEX [IX_OperationalEvents_DepartmentId_OccurredAtUtc] ON [bekp].[OperationalEvents] ([DepartmentId], [OccurredAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    CREATE INDEX [IX_OperationalEvents_OccurredAtUtc] ON [bekp].[OperationalEvents] ([OccurredAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    CREATE INDEX [IX_ProcessTimings_CompletedAtUtc_DepartmentId] ON [bekp].[ProcessTimings] ([CompletedAtUtc], [DepartmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    CREATE INDEX [IX_ProcessTimings_DepartmentId] ON [bekp].[ProcessTimings] ([DepartmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [bekp].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261118040354_InitialInpatientOperations'
)
BEGIN
    INSERT INTO [bekp].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261118040354_InitialInpatientOperations', N'10.0.15');
END;

COMMIT;
GO


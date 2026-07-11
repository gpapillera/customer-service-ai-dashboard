-- ============================================================================
-- Customer Service AI Dashboard — Database Schema (Microsoft SQL Server)
-- ----------------------------------------------------------------------------
-- Run this script against a SQL Server instance (or let EF Core migrations
-- create the schema). The ASP.NET Core backend uses EF Core Code-First; this
-- file is provided as a reference / for manual setup and for the Python
-- data-cleaning pipeline to target.
--
-- Compatible with: SQL Server 2019+ / Azure SQL Database.
-- ============================================================================

IF OBJECT_ID('CallLogs', 'U') IS NOT NULL DROP TABLE CallLogs;
IF OBJECT_ID('Cases', 'U') IS NOT NULL DROP TABLE Cases;
IF OBJECT_ID('Categories', 'U') IS NOT NULL DROP TABLE Categories;
IF OBJECT_ID('Customers', 'U') IS NOT NULL DROP TABLE Customers;
IF OBJECT_ID('Users', 'U') IS NOT NULL DROP TABLE Users;
GO

CREATE TABLE Users (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    FullName NVARCHAR(150) NOT NULL,
    Email NVARCHAR(150) NOT NULL UNIQUE,
    PasswordHash NVARCHAR(255) NOT NULL,
    Role NVARCHAR(20) NOT NULL DEFAULT 'Agent',   -- Admin | Agent
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE Customers (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    FullName NVARCHAR(150) NOT NULL,
    Email NVARCHAR(150) NULL,
    Phone NVARCHAR(30) NULL,
    Company NVARCHAR(150) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2 NULL
);

CREATE TABLE Categories (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL UNIQUE,
    Description NVARCHAR(255) NULL
);

CREATE TABLE Cases (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    CustomerId INT NOT NULL FOREIGN KEY REFERENCES Customers(Id),
    CategoryId INT NOT NULL FOREIGN KEY REFERENCES Categories(Id),
    AssignedAgentId INT NULL FOREIGN KEY REFERENCES Users(Id),
    Subject NVARCHAR(200) NOT NULL,
    Description NVARCHAR(MAX) NULL,
    Status NVARCHAR(20) NOT NULL DEFAULT 'Open',        -- Open | InProgress | Resolved | Closed
    Priority NVARCHAR(10) NOT NULL DEFAULT 'Medium',    -- Low | Medium | High (agent-confirmed)
    PredictedPriority NVARCHAR(10) NULL,                -- Low | Medium | High (model output)
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    DueDate DATETIME2 NULL,
    ResolvedAt DATETIME2 NULL
);

CREATE TABLE CallLogs (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    CaseId INT NOT NULL FOREIGN KEY REFERENCES Cases(Id),
    AgentId INT NOT NULL FOREIGN KEY REFERENCES Users(Id),
    ContactType NVARCHAR(20) NOT NULL,   -- Call | Email | InPerson | Chat
    Notes NVARCHAR(MAX) NULL,
    Outcome NVARCHAR(100) NULL,
    FollowUpDate DATETIME2 NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);

-- Indexes to support common dashboard / list queries.
CREATE INDEX IX_Cases_Status ON Cases(Status);
CREATE INDEX IX_Cases_Priority ON Cases(Priority);
CREATE INDEX IX_Cases_CategoryId ON Cases(CategoryId);
CREATE INDEX IX_Cases_CreatedAt ON Cases(CreatedAt);
CREATE INDEX IX_CallLogs_CaseId ON CallLogs(CaseId);
GO

-- ----------------------------------------------------------------------------
-- Seed data: complaint categories (required reference data).
-- A demo Admin user is seeded by the EF Core SeedData class in the backend
-- (password hashed with BCrypt) so credentials are never stored in SQL here.
-- ----------------------------------------------------------------------------
INSERT INTO Categories (Name, Description) VALUES
    ('Billing', 'Invoices, payments, refunds, and billing disputes.'),
    ('Technical Support', 'Product or service technical faults and how-to help.'),
    ('Shipping / Supply Chain', 'Delivery delays, stock-outs, and logistics issues.'),
    ('Product Quality', 'Defects, damage, and quality complaints.'),
    ('General Inquiry', 'Non-urgent questions and information requests.');
GO

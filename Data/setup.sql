-- ============================================
-- Online Registration System - Database Setup
-- Run this script in SQL Server Management Studio
-- ============================================

-- 1. Create the database
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'OnlineRegDB')
BEGIN
    CREATE DATABASE OnlineRegDB;
    PRINT 'Database OnlineRegDB created.';
END
GO

USE OnlineRegDB;
GO

-- 2. Create the Users table
IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Users' AND xtype='U')
BEGIN
    CREATE TABLE Users (
        UserID INT PRIMARY KEY IDENTITY(1,1),
        FullName NVARCHAR(100) NOT NULL,
        Email NVARCHAR(100) UNIQUE NOT NULL,
        PasswordHash NVARCHAR(255) NOT NULL,
        Course NVARCHAR(50),
        Role NVARCHAR(20) DEFAULT 'Student',
        CreatedAt DATETIME DEFAULT GETDATE()
    );
    PRINT 'Users table created.';
END
GO

-- 3. Seed an Admin user (password: Admin@123)
-- BCrypt hash of "Admin@123" — generate a fresh one in production
-- This is a placeholder; the app will hash on registration.
-- You can insert directly if needed:
/*
INSERT INTO Users (FullName, Email, PasswordHash, Course, Role)
VALUES ('System Admin', 'admin@system.com', 
        '$2a$11$placeholder_hash_replace_me', 
        'N/A', 'Admin');
*/

PRINT 'Database setup complete.';
GO

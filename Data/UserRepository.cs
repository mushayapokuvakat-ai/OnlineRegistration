using Microsoft.Data.SqlClient;
using OnlineRegistrationSystem.Models;

namespace OnlineRegistrationSystem.Data
{
    /// <summary>
    /// Data access layer for User operations using ADO.NET.
    /// </summary>
    public class UserRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<UserRepository> _logger;

        public UserRepository(IConfiguration config, ILogger<UserRepository> logger)
        {
            _connectionString = config.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _logger = logger;
        }

        /// <summary>
        /// Ensures the database exists.
        /// </summary>
        public async Task EnsureDatabaseExistsAsync()
        {
            var masterConnectionString = _connectionString.Replace("Database=OnlineRegDB", "Database=master");
            const string checkDbSql = @"
                IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'OnlineRegDB')
                BEGIN
                    CREATE DATABASE OnlineRegDB;
                END";

            await using var con = new SqlConnection(masterConnectionString);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(checkDbSql, con);
            await cmd.ExecuteNonQueryAsync();
            _logger.LogInformation("Database 'OnlineRegDB' checked/created.");
        }

        /// <summary>
        /// Creates all tables if they do not already exist and seeds default data.
        /// </summary>
        public async Task EnsureTableExistsAsync()
        {
            // Delay slightly to ensure DB is ready if just created
            await Task.Delay(1000);

            const string sql = @"
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Users' AND xtype='U')
                CREATE TABLE Users (
                    UserID INT PRIMARY KEY IDENTITY(1,1),
                    FullName NVARCHAR(100) NOT NULL,
                    Email NVARCHAR(100) UNIQUE NOT NULL,
                    PasswordHash NVARCHAR(255) NOT NULL,
                    Course NVARCHAR(50),
                    Role NVARCHAR(20) DEFAULT 'Student',
                    CreatedAt DATETIME DEFAULT GETDATE()
                );

                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Courses' AND xtype='U')
                CREATE TABLE Courses (
                    CourseID INT PRIMARY KEY IDENTITY(1,1),
                    CourseName NVARCHAR(100) NOT NULL,
                    CourseCode NVARCHAR(20) NOT NULL,
                    Description NVARCHAR(500),
                    LecturerID INT NULL,
                    MaxCapacity INT DEFAULT 100,
                    IsActive BIT DEFAULT 1,
                    FOREIGN KEY (LecturerID) REFERENCES Users(UserID)
                );

                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Enrollments' AND xtype='U')
                CREATE TABLE Enrollments (
                    EnrollmentID INT PRIMARY KEY IDENTITY(1,1),
                    StudentID INT NOT NULL,
                    CourseID INT NOT NULL,
                    EnrolledAt DATETIME DEFAULT GETDATE(),
                    FOREIGN KEY (StudentID) REFERENCES Users(UserID),
                    FOREIGN KEY (CourseID) REFERENCES Courses(CourseID),
                    CONSTRAINT UQ_Enrollment UNIQUE (StudentID, CourseID)
                );

                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='RegistrationSettings' AND xtype='U')
                CREATE TABLE RegistrationSettings (
                    SettingKey NVARCHAR(50) PRIMARY KEY,
                    SettingValue NVARCHAR(200) NOT NULL
                );";

            await using var con = new SqlConnection(_connectionString);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            await cmd.ExecuteNonQueryAsync();
            _logger.LogInformation("All tables verified/created.");

            // Migration: Add IsActive to Courses if missing
            var checkCol = "SELECT COUNT(*) FROM sys.columns WHERE Name = N'IsActive' AND Object_ID = Object_ID(N'Courses')";
            await using (var cmdCol = new SqlCommand(checkCol, con))
            {
                var colExists = (int)await cmdCol.ExecuteScalarAsync() > 0;
                if (!colExists)
                {
                    var addCol = "ALTER TABLE Courses ADD IsActive BIT DEFAULT 1 WITH VALUES;";
                    await using var cmdAdd = new SqlCommand(addCol, con);
                    await cmdAdd.ExecuteNonQueryAsync();
                    _logger.LogInformation("Added IsActive column to Courses.");
                }
            }

            // Seed default data
            await SeedDefaultDataAsync(con);
        }

        /// <summary>
        /// Seeds default courses, lecturers, admin, and registration settings.
        /// </summary>
        private async Task SeedDefaultDataAsync(SqlConnection con)
        {
            // Seed or Update Admin
            var adminEmail = "patnat@system.com";
            var adminPass = "Pyruvate#13";
            var adminHash = BCrypt.Net.BCrypt.HashPassword(adminPass);

            var checkAdminSql = "SELECT COUNT(*) FROM Users WHERE Email = @Email";
            await using (var checkCmd = new SqlCommand(checkAdminSql, con))
            {
                checkCmd.Parameters.AddWithValue("@Email", adminEmail);
                var exists = (int)await checkCmd.ExecuteScalarAsync() > 0;

                if (exists)
                {
                    // Update password if admin exists
                    var updateSql = "UPDATE Users SET PasswordHash = @Hash, FullName = 'Patnat' WHERE Email = @Email";
                    await using var updateCmd = new SqlCommand(updateSql, con);
                    updateCmd.Parameters.AddWithValue("@Hash", adminHash);
                    updateCmd.Parameters.AddWithValue("@Email", adminEmail);
                    await updateCmd.ExecuteNonQueryAsync();
                    _logger.LogInformation("Admin password updated.");
                }
                else
                {
                    // Insert new admin
                    var insertAdmin = @"INSERT INTO Users (FullName, Email, PasswordHash, Course, Role)
                                        VALUES ('Patnat', @Email, @Hash, 'N/A', 'Admin');";
                    await using var insertCmd = new SqlCommand(insertAdmin, con);
                    insertCmd.Parameters.AddWithValue("@Email", adminEmail);
                    insertCmd.Parameters.AddWithValue("@Hash", adminHash);
                    await insertCmd.ExecuteNonQueryAsync();
                    _logger.LogInformation("Admin user seeded.");
                }
            }

            // Seed lecturers if not exists
            var lecturers = new[]
            {
                ("Prof. Marie Dupont", "marie.dupont@university.com"),
                ("Dr. Alan Turing Jr.", "alan.turing@university.com"),
                ("Prof. Grace Hopper", "grace.hopper@university.com"),
                ("Dr. Ian Sommerville", "ian.sommerville@university.com"),
                ("Prof. Andrew Ng", "andrew.ng@university.com"),
                ("Dr. James Gosling", "james.gosling@university.com")
            };

            foreach (var (name, email) in lecturers)
            {
                var checkLec = "SELECT COUNT(*) FROM Users WHERE Email = @Email;";
                await using var checkCmd = new SqlCommand(checkLec, con);
                checkCmd.Parameters.AddWithValue("@Email", email);
                var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                if (exists == 0)
                {
                    var hash = BCrypt.Net.BCrypt.HashPassword("Lecturer@123");
                    var insertLec = @"INSERT INTO Users (FullName, Email, PasswordHash, Course, Role)
                                      VALUES (@Name, @Email, @Hash, 'N/A', 'Lecturer');";
                    await using var insertCmd = new SqlCommand(insertLec, con);
                    insertCmd.Parameters.AddWithValue("@Name", name);
                    insertCmd.Parameters.AddWithValue("@Email", email);
                    insertCmd.Parameters.AddWithValue("@Hash", hash);
                    await insertCmd.ExecuteNonQueryAsync();
                }
            }

            // Seed courses (idempotent, ensures defaults exist and are active)
            var defaultCourses = new[]
            {
                ("French", "FRN101", "Learn French language fundamentals, grammar, and conversation skills.", "marie.dupont@university.com"),
                ("Calculus", "MAT201", "Differential and integral calculus, limits, and series.", "alan.turing@university.com"),
                ("Theory of Computing", "CSC301", "Automata theory, formal languages, computability, and complexity.", "grace.hopper@university.com"),
                ("Introduction to Software Engineering", "SEN101", "Software development lifecycle, methodologies, and best practices.", "ian.sommerville@university.com"),
                ("Fundamentals of Artificial Intelligence", "AIT201", "Search algorithms, knowledge representation, machine learning basics.", "andrew.ng@university.com"),
                ("Object Oriented Software Development", "OOP301", "OOP principles, design patterns, and SOLID principles using modern languages.", "james.gosling@university.com")
            };

            // Get lecturer IDs
            var lecIds = new Dictionary<string, int>();
            var getLecSql = "SELECT UserID, Email FROM Users WHERE Role = 'Lecturer';";
            await using (var getLecCmd = new SqlCommand(getLecSql, con))
            await using (var reader = await getLecCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    lecIds[reader.GetString(1)] = reader.GetInt32(0); // Email -> ID
                }
            }

            foreach (var (name, code, desc, lecEmail) in defaultCourses)
            {
                // Check if course exists (by code)
                var checkCourse = "SELECT COUNT(*) FROM Courses WHERE CourseCode = @Code;";
                await using var checkCmd = new SqlCommand(checkCourse, con);
                checkCmd.Parameters.AddWithValue("@Code", code);
                var exists = (int)await checkCmd.ExecuteScalarAsync() > 0;

                if (!exists)
                {
                    // Resolve lecturer ID
                    object lecIdDb = DBNull.Value;
                    if (lecIds.TryGetValue(lecEmail, out var id))
                    {
                        lecIdDb = id;
                    }

                    var insertCourse = @"INSERT INTO Courses (CourseName, CourseCode, Description, LecturerID, MaxCapacity, IsActive)
                                         VALUES (@Name, @Code, @Desc, @LecID, 100, 1);";
                    await using var insertCmd = new SqlCommand(insertCourse, con);
                    insertCmd.Parameters.AddWithValue("@Name", name);
                    insertCmd.Parameters.AddWithValue("@Code", code);
                    insertCmd.Parameters.AddWithValue("@Desc", desc);
                    insertCmd.Parameters.AddWithValue("@LecID", lecIdDb);
                    await insertCmd.ExecuteNonQueryAsync();
                    _logger.LogInformation($"Seeded course: {name}");
                }
                else
                {
                    // Ensure active
                    var updateActive = "UPDATE Courses SET IsActive = 1 WHERE CourseCode = @Code AND IsActive = 0;";
                    await using var updateCmd = new SqlCommand(updateActive, con);
                    updateCmd.Parameters.AddWithValue("@Code", code);
                    await updateCmd.ExecuteNonQueryAsync();
                }
            }

            // Seed registration settings
            var settingCheck = "SELECT COUNT(*) FROM RegistrationSettings;";
            await using (var cmd = new SqlCommand(settingCheck, con))
            {
                var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                if (count == 0)
                {
                    var settings = new[]
                    {
                        ("RegistrationOpen", "true"),
                        ("MaxCoursesPerStudent", "5")
                    };
                    foreach (var (key, val) in settings)
                    {
                        var insertSetting = "INSERT INTO RegistrationSettings (SettingKey, SettingValue) VALUES (@Key, @Val);";
                        await using var insertCmd = new SqlCommand(insertSetting, con);
                        insertCmd.Parameters.AddWithValue("@Key", key);
                        insertCmd.Parameters.AddWithValue("@Val", val);
                        await insertCmd.ExecuteNonQueryAsync();
                    }
                    _logger.LogInformation("Default registration settings seeded.");
                }
            }
        }

        /// <summary>
        /// Inserts a new user record into the database.
        /// Returns the new UserID.
        /// </summary>
        public async Task<int> CreateUserAsync(string fullName, string email, string passwordHash, string course, string role = "Student")
        {
            const string sql = @"
                INSERT INTO Users (FullName, Email, PasswordHash, Course, Role)
                OUTPUT INSERTED.UserID
                VALUES (@FullName, @Email, @PasswordHash, @Course, @Role);";

            await using var con = new SqlConnection(_connectionString);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@FullName", fullName);
            cmd.Parameters.AddWithValue("@Email", email);
            cmd.Parameters.AddWithValue("@PasswordHash", passwordHash);
            cmd.Parameters.AddWithValue("@Course", course);
            cmd.Parameters.AddWithValue("@Role", role);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        /// <summary>
        /// Retrieves a user by email address.
        /// </summary>
        public async Task<User?> GetUserByEmailAsync(string email)
        {
            const string sql = "SELECT UserID, FullName, Email, PasswordHash, Course, Role, CreatedAt FROM Users WHERE Email = @Email;";

            await using var con = new SqlConnection(_connectionString);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Email", email);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new User
                {
                    UserID = reader.GetInt32(0),
                    FullName = reader.GetString(1),
                    Email = reader.GetString(2),
                    PasswordHash = reader.GetString(3),
                    Course = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    Role = reader.IsDBNull(5) ? "Student" : reader.GetString(5),
                    CreatedAt = reader.GetDateTime(6)
                };
            }

            return null;
        }

        /// <summary>
        /// Checks if an email is already registered.
        /// </summary>
        public async Task<bool> EmailExistsAsync(string email)
        {
            const string sql = "SELECT COUNT(1) FROM Users WHERE Email = @Email;";

            await using var con = new SqlConnection(_connectionString);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Email", email);

            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            return count > 0;
        }

        /// <summary>
        /// Retrieves all registered users (for admin dashboard).
        /// </summary>
        public async Task<List<User>> GetAllUsersAsync()
        {
            const string sql = "SELECT UserID, FullName, Email, PasswordHash, Course, Role, CreatedAt FROM Users ORDER BY CreatedAt DESC;";

            var users = new List<User>();

            await using var con = new SqlConnection(_connectionString);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                users.Add(new User
                {
                    UserID = reader.GetInt32(0),
                    FullName = reader.GetString(1),
                    Email = reader.GetString(2),
                    PasswordHash = "***", // Never expose hashes
                    Course = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    Role = reader.IsDBNull(5) ? "Student" : reader.GetString(5),
                    CreatedAt = reader.GetDateTime(6)
                });
            }

            return users;
        }

        /// <summary>
        /// Deletes a user by ID (admin function).
        /// </summary>
        public async Task<bool> DeleteUserAsync(int userId)
        {
            const string sql = "DELETE FROM Users WHERE UserID = @UserID;";

            await using var con = new SqlConnection(_connectionString);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@UserID", userId);

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }
    }
}

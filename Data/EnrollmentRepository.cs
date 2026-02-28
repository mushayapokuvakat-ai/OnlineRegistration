using Microsoft.Data.SqlClient;
using OnlineRegistrationSystem.Models;

namespace OnlineRegistrationSystem.Data
{
    /// <summary>
    /// Data access layer for Enrollment operations and Registration Settings.
    /// </summary>
    public class EnrollmentRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<EnrollmentRepository> _logger;

        public EnrollmentRepository(IConfiguration config, ILogger<EnrollmentRepository> logger)
        {
            _connectionString = config.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _logger = logger;
        }

        /// <summary>
        /// Enrolls a student in a course. Returns the new EnrollmentID.
        /// Checks max-courses limit and capacity before inserting.
        /// </summary>
        public async Task<int> EnrollStudentAsync(int studentId, int courseId)
        {
            // Get max courses per student from settings
            var maxCourses = await GetRegistrationSettingIntAsync("MaxCoursesPerStudent", 5);

            await using var con = new SqlConnection(_connectionString);
            await con.OpenAsync();

            // Check if registration is open
            var regOpen = await GetRegistrationSettingAsync("RegistrationOpen");
            if (regOpen != null && regOpen.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Registration is currently closed.");
            }

            // Check current enrollment count for this student
            var countSql = "SELECT COUNT(*) FROM Enrollments WHERE StudentID = @StudentID;";
            await using (var countCmd = new SqlCommand(countSql, con))
            {
                countCmd.Parameters.AddWithValue("@StudentID", studentId);
                var currentCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
                if (currentCount >= maxCourses)
                {
                    throw new InvalidOperationException($"You cannot register for more than {maxCourses} courses.");
                }
            }

            // Check if already enrolled
            var dupSql = "SELECT COUNT(*) FROM Enrollments WHERE StudentID = @StudentID AND CourseID = @CourseID;";
            await using (var dupCmd = new SqlCommand(dupSql, con))
            {
                dupCmd.Parameters.AddWithValue("@StudentID", studentId);
                dupCmd.Parameters.AddWithValue("@CourseID", courseId);
                var dup = Convert.ToInt32(await dupCmd.ExecuteScalarAsync());
                if (dup > 0)
                {
                    throw new InvalidOperationException("You are already enrolled in this course.");
                }
            }

            // Check course capacity
            var capSql = @"
                SELECT c.MaxCapacity, (SELECT COUNT(*) FROM Enrollments e WHERE e.CourseID = c.CourseID)
                FROM Courses c WHERE c.CourseID = @CourseID AND c.IsActive = 1;";
            await using (var capCmd = new SqlCommand(capSql, con))
            {
                capCmd.Parameters.AddWithValue("@CourseID", courseId);
                await using var capReader = await capCmd.ExecuteReaderAsync();
                if (await capReader.ReadAsync())
                {
                    var maxCap = capReader.GetInt32(0);
                    var enrolled = capReader.GetInt32(1);
                    if (enrolled >= maxCap)
                    {
                        throw new InvalidOperationException("This course is full.");
                    }
                }
                else
                {
                    throw new InvalidOperationException("Course not found or inactive.");
                }
            }

            // Insert enrollment
            const string sql = @"
                INSERT INTO Enrollments (StudentID, CourseID)
                OUTPUT INSERTED.EnrollmentID
                VALUES (@StudentID, @CourseID);";

            await using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@StudentID", studentId);
            cmd.Parameters.AddWithValue("@CourseID", courseId);

            var result = await cmd.ExecuteScalarAsync();
            _logger.LogInformation("Student {StudentID} enrolled in Course {CourseID}", studentId, courseId);
            return Convert.ToInt32(result);
        }

        /// <summary>
        /// Unenrolls a student from a course.
        /// </summary>
        public async Task<bool> UnenrollStudentAsync(int studentId, int courseId)
        {
            const string sql = "DELETE FROM Enrollments WHERE StudentID = @StudentID AND CourseID = @CourseID;";

            await using var con = new SqlConnection(_connectionString);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@StudentID", studentId);
            cmd.Parameters.AddWithValue("@CourseID", courseId);

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }

        /// <summary>
        /// Gets all courses a student is enrolled in.
        /// </summary>
        public async Task<List<Enrollment>> GetStudentEnrollmentsAsync(int studentId)
        {
            const string sql = @"
                SELECT e.EnrollmentID, e.StudentID, u.FullName, u.Email,
                       e.CourseID, c.CourseName, c.CourseCode, e.EnrolledAt
                FROM Enrollments e
                INNER JOIN Users u ON e.StudentID = u.UserID
                INNER JOIN Courses c ON e.CourseID = c.CourseID
                WHERE e.StudentID = @StudentID
                ORDER BY c.CourseName;";

            var enrollments = new List<Enrollment>();
            await using var con = new SqlConnection(_connectionString);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@StudentID", studentId);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                enrollments.Add(new Enrollment
                {
                    EnrollmentID = reader.GetInt32(0),
                    StudentID = reader.GetInt32(1),
                    StudentName = reader.GetString(2),
                    StudentEmail = reader.GetString(3),
                    CourseID = reader.GetInt32(4),
                    CourseName = reader.GetString(5),
                    CourseCode = reader.GetString(6),
                    EnrolledAt = reader.GetDateTime(7)
                });
            }
            return enrollments;
        }

        /// <summary>
        /// Gets a registration setting value by key.
        /// </summary>
        public async Task<string?> GetRegistrationSettingAsync(string key)
        {
            const string sql = "SELECT SettingValue FROM RegistrationSettings WHERE SettingKey = @Key;";

            await using var con = new SqlConnection(_connectionString);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Key", key);

            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString();
        }

        /// <summary>
        /// Gets a registration setting as integer.
        /// </summary>
        public async Task<int> GetRegistrationSettingIntAsync(string key, int defaultValue)
        {
            var val = await GetRegistrationSettingAsync(key);
            return int.TryParse(val, out var result) ? result : defaultValue;
        }

        /// <summary>
        /// Sets a registration setting value.
        /// </summary>
        public async Task SetRegistrationSettingAsync(string key, string value)
        {
            const string sql = @"
                IF EXISTS (SELECT 1 FROM RegistrationSettings WHERE SettingKey = @Key)
                    UPDATE RegistrationSettings SET SettingValue = @Value WHERE SettingKey = @Key
                ELSE
                    INSERT INTO RegistrationSettings (SettingKey, SettingValue) VALUES (@Key, @Value);";

            await using var con = new SqlConnection(_connectionString);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@Key", key);
            cmd.Parameters.AddWithValue("@Value", value);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Gets all registration settings as a dictionary.
        /// </summary>
        public async Task<Dictionary<string, string>> GetAllSettingsAsync()
        {
            const string sql = "SELECT SettingKey, SettingValue FROM RegistrationSettings;";

            var settings = new Dictionary<string, string>();
            await using var con = new SqlConnection(_connectionString);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                settings[reader.GetString(0)] = reader.GetString(1);
            }
            return settings;
        }
    }
}

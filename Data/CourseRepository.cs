using Microsoft.Data.SqlClient;
using OnlineRegistrationSystem.Models;

namespace OnlineRegistrationSystem.Data
{
    /// <summary>
    /// Data access layer for Course operations using ADO.NET.
    /// </summary>
    public class CourseRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<CourseRepository> _logger;

        public CourseRepository(IConfiguration config, ILogger<CourseRepository> logger)
        {
            _connectionString = config.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            _logger = logger;
        }

        /// <summary>
        /// Retrieves all active courses with lecturer names and enrollment counts.
        /// </summary>
        public async Task<List<Course>> GetAllCoursesAsync()
        {
            const string sql = @"
                SELECT c.CourseID, c.CourseName, c.CourseCode, c.Description,
                       c.LecturerID, ISNULL(u.FullName, 'TBA') AS LecturerName,
                       c.MaxCapacity, c.IsActive,
                       (SELECT COUNT(*) FROM Enrollments e WHERE e.CourseID = c.CourseID) AS EnrolledCount
                FROM Courses c
                LEFT JOIN Users u ON c.LecturerID = u.UserID
                WHERE c.IsActive = 1
                ORDER BY c.CourseName;";

            var courses = new List<Course>();
            await using var con = new SqlConnection(_connectionString);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                courses.Add(new Course
                {
                    CourseID = reader.GetInt32(0),
                    CourseName = reader.GetString(1),
                    CourseCode = reader.GetString(2),
                    Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    LecturerID = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    LecturerName = reader.GetString(5),
                    MaxCapacity = reader.GetInt32(6),
                    IsActive = reader.GetBoolean(7),
                    EnrolledCount = reader.GetInt32(8)
                });
            }
            return courses;
        }

        /// <summary>
        /// Retrieves a course by ID.
        /// </summary>
        public async Task<Course?> GetCourseByIdAsync(int courseId)
        {
            const string sql = @"
                SELECT c.CourseID, c.CourseName, c.CourseCode, c.Description,
                       c.LecturerID, ISNULL(u.FullName, 'TBA') AS LecturerName,
                       c.MaxCapacity, c.IsActive,
                       (SELECT COUNT(*) FROM Enrollments e WHERE e.CourseID = c.CourseID) AS EnrolledCount
                FROM Courses c
                LEFT JOIN Users u ON c.LecturerID = u.UserID
                WHERE c.CourseID = @CourseID;";

            await using var con = new SqlConnection(_connectionString);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@CourseID", courseId);
            await using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new Course
                {
                    CourseID = reader.GetInt32(0),
                    CourseName = reader.GetString(1),
                    CourseCode = reader.GetString(2),
                    Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    LecturerID = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    LecturerName = reader.GetString(5),
                    MaxCapacity = reader.GetInt32(6),
                    IsActive = reader.GetBoolean(7),
                    EnrolledCount = reader.GetInt32(8)
                };
            }
            return null;
        }

        /// <summary>
        /// Creates a new course.
        /// </summary>
        public async Task<int> AddCourseAsync(string courseName, string courseCode, string description, int? lecturerId, int maxCapacity)
        {
            const string sql = @"
                INSERT INTO Courses (CourseName, CourseCode, Description, LecturerID, MaxCapacity)
                OUTPUT INSERTED.CourseID
                VALUES (@CourseName, @CourseCode, @Description, @LecturerID, @MaxCapacity);";

            await using var con = new SqlConnection(_connectionString);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@CourseName", courseName);
            cmd.Parameters.AddWithValue("@CourseCode", courseCode);
            cmd.Parameters.AddWithValue("@Description", description);
            cmd.Parameters.AddWithValue("@LecturerID", (object?)lecturerId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@MaxCapacity", maxCapacity);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        /// <summary>
        /// Deletes (deactivates) a course by ID.
        /// </summary>
        public async Task<bool> DeleteCourseAsync(int courseId)
        {
            const string sql = "UPDATE Courses SET IsActive = 0 WHERE CourseID = @CourseID;";

            await using var con = new SqlConnection(_connectionString);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@CourseID", courseId);

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }

        /// <summary>
        /// Gets courses assigned to a specific lecturer.
        /// </summary>
        public async Task<List<Course>> GetCoursesByLecturerAsync(int lecturerId)
        {
            const string sql = @"
                SELECT c.CourseID, c.CourseName, c.CourseCode, c.Description,
                       c.LecturerID, ISNULL(u.FullName, 'TBA') AS LecturerName,
                       c.MaxCapacity, c.IsActive,
                       (SELECT COUNT(*) FROM Enrollments e WHERE e.CourseID = c.CourseID) AS EnrolledCount
                FROM Courses c
                LEFT JOIN Users u ON c.LecturerID = u.UserID
                WHERE c.LecturerID = @LecturerID AND c.IsActive = 1
                ORDER BY c.CourseName;";

            var courses = new List<Course>();
            await using var con = new SqlConnection(_connectionString);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@LecturerID", lecturerId);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                courses.Add(new Course
                {
                    CourseID = reader.GetInt32(0),
                    CourseName = reader.GetString(1),
                    CourseCode = reader.GetString(2),
                    Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    LecturerID = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    LecturerName = reader.GetString(5),
                    MaxCapacity = reader.GetInt32(6),
                    IsActive = reader.GetBoolean(7),
                    EnrolledCount = reader.GetInt32(8)
                });
            }
            return courses;
        }

        /// <summary>
        /// Gets students enrolled in a specific course.
        /// </summary>
        public async Task<List<Enrollment>> GetEnrolledStudentsAsync(int courseId)
        {
            const string sql = @"
                SELECT e.EnrollmentID, e.StudentID, u.FullName, u.Email,
                       e.CourseID, c.CourseName, c.CourseCode, e.EnrolledAt
                FROM Enrollments e
                INNER JOIN Users u ON e.StudentID = u.UserID
                INNER JOIN Courses c ON e.CourseID = c.CourseID
                WHERE e.CourseID = @CourseID
                ORDER BY u.FullName;";

            var enrollments = new List<Enrollment>();
            await using var con = new SqlConnection(_connectionString);
            await con.OpenAsync();
            await using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.AddWithValue("@CourseID", courseId);
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
    }
}

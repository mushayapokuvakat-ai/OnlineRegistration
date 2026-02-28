using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineRegistrationSystem.Data;
using OnlineRegistrationSystem.Models;

namespace OnlineRegistrationSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")]
    public class AdminController : ControllerBase
    {
        private readonly UserRepository _userRepo;
        private readonly CourseRepository _courseRepo;
        private readonly EnrollmentRepository _enrollmentRepo;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            UserRepository userRepo,
            CourseRepository courseRepo,
            EnrollmentRepository enrollmentRepo,
            ILogger<AdminController> logger)
        {
            _userRepo = userRepo;
            _courseRepo = courseRepo;
            _enrollmentRepo = enrollmentRepo;
            _logger = logger;
        }

        // ─── User Management ───────────────────────────────────────

        /// <summary>
        /// GET /api/admin/users — Returns all registered users (Admin only).
        /// </summary>
        [HttpGet("users")]
        public async Task<IActionResult> GetAllUsers()
        {
            try
            {
                var users = await _userRepo.GetAllUsersAsync();
                return Ok(ApiResponse<List<User>>.Ok(users, $"Found {users.Count} registered user(s)."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving users.");
                return StatusCode(500, ApiResponse<object>.Fail("Failed to retrieve users."));
            }
        }

        /// <summary>
        /// DELETE /api/admin/users/{id} — Deletes a user by ID (Admin only).
        /// </summary>
        [HttpDelete("users/{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            try
            {
                var deleted = await _userRepo.DeleteUserAsync(id);
                if (!deleted)
                    return NotFound(ApiResponse<object>.Fail("User not found."));

                _logger.LogInformation("Admin deleted user {UserID}", id);
                return Ok(ApiResponse<object>.Ok(new { deletedId = id }, "User deleted successfully."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting user {UserID}", id);
                return StatusCode(500, ApiResponse<object>.Fail("Failed to delete user."));
            }
        }

        // ─── Course Management ─────────────────────────────────────

        /// <summary>
        /// GET /api/admin/courses — List all courses (active and inactive).
        /// </summary>
        [HttpGet("courses")]
        public async Task<IActionResult> GetAllCourses()
        {
            try
            {
                var courses = await _courseRepo.GetAllCoursesAsync();
                return Ok(ApiResponse<List<Course>>.Ok(courses, $"Found {courses.Count} course(s)."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching courses.");
                return StatusCode(500, ApiResponse<object>.Fail("Failed to load courses."));
            }
        }

        /// <summary>
        /// POST /api/admin/courses — Add a new course.
        /// </summary>
        [HttpPost("courses")]
        public async Task<IActionResult> AddCourse([FromBody] CreateCourseRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ApiResponse<object>.Fail("Validation failed."));

            try
            {
                var courseId = await _courseRepo.AddCourseAsync(
                    request.CourseName,
                    request.CourseCode,
                    request.Description,
                    request.LecturerID,
                    request.MaxCapacity);

                _logger.LogInformation("Admin added course {CourseName} (ID: {CourseID})", request.CourseName, courseId);
                return Ok(ApiResponse<object>.Ok(new { courseId }, "Course added successfully."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding course.");
                return StatusCode(500, ApiResponse<object>.Fail("Failed to add course."));
            }
        }

        /// <summary>
        /// DELETE /api/admin/courses/{id} — Remove (deactivate) a course.
        /// </summary>
        [HttpDelete("courses/{id}")]
        public async Task<IActionResult> DeleteCourse(int id)
        {
            try
            {
                var deleted = await _courseRepo.DeleteCourseAsync(id);
                if (!deleted)
                    return NotFound(ApiResponse<object>.Fail("Course not found."));

                _logger.LogInformation("Admin removed course {CourseID}", id);
                return Ok(ApiResponse<object>.Ok(new { deletedId = id }, "Course removed successfully."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing course {CourseID}", id);
                return StatusCode(500, ApiResponse<object>.Fail("Failed to remove course."));
            }
        }

        // ─── Registration Settings ─────────────────────────────────

        /// <summary>
        /// GET /api/admin/settings — Get all registration settings.
        /// </summary>
        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings()
        {
            try
            {
                var settings = await _enrollmentRepo.GetAllSettingsAsync();
                return Ok(ApiResponse<Dictionary<string, string>>.Ok(settings));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching settings.");
                return StatusCode(500, ApiResponse<object>.Fail("Failed to load settings."));
            }
        }

        /// <summary>
        /// PUT /api/admin/settings — Update registration settings.
        /// </summary>
        [HttpPut("settings")]
        public async Task<IActionResult> UpdateSettings([FromBody] UpdateSettingsRequest request)
        {
            try
            {
                await _enrollmentRepo.SetRegistrationSettingAsync("RegistrationOpen", request.RegistrationOpen.ToString().ToLower());
                await _enrollmentRepo.SetRegistrationSettingAsync("MaxCoursesPerStudent", request.MaxCoursesPerStudent.ToString());

                _logger.LogInformation("Admin updated settings: RegistrationOpen={Open}, MaxCourses={Max}",
                    request.RegistrationOpen, request.MaxCoursesPerStudent);

                return Ok(ApiResponse<object>.Ok(new { }, "Settings updated successfully."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating settings.");
                return StatusCode(500, ApiResponse<object>.Fail("Failed to update settings."));
            }
        }
    }
}

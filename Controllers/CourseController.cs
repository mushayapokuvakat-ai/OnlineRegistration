using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineRegistrationSystem.Data;
using OnlineRegistrationSystem.Models;

namespace OnlineRegistrationSystem.Controllers
{
    [ApiController]
    [Route("api/courses")]
    public class CourseController : ControllerBase
    {
        private readonly CourseRepository _courseRepo;
        private readonly EnrollmentRepository _enrollmentRepo;
        private readonly ILogger<CourseController> _logger;

        public CourseController(
            CourseRepository courseRepo,
            EnrollmentRepository enrollmentRepo,
            ILogger<CourseController> logger)
        {
            _courseRepo = courseRepo;
            _enrollmentRepo = enrollmentRepo;
            _logger = logger;
        }

        /// <summary>
        /// GET /api/courses — List all available courses (public).
        /// </summary>
        [HttpGet]
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
        /// GET /api/courses/my-courses — Get current student's enrolled courses.
        /// </summary>
        [HttpGet("my-courses")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> GetMyCourses()
        {
            try
            {
                var studentId = GetUserId();
                var enrollments = await _enrollmentRepo.GetStudentEnrollmentsAsync(studentId);
                return Ok(ApiResponse<List<Enrollment>>.Ok(enrollments, $"You are enrolled in {enrollments.Count} course(s)."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching student courses.");
                return StatusCode(500, ApiResponse<object>.Fail("Failed to load your courses."));
            }
        }

        /// <summary>
        /// POST /api/courses/enroll — Enroll student in courses.
        /// </summary>
        [HttpPost("enroll")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> Enroll([FromBody] EnrollRequest request)
        {
            if (!ModelState.IsValid || request.CourseIds.Count == 0)
                return BadRequest(ApiResponse<object>.Fail("Please select at least one course."));

            try
            {
                var studentId = GetUserId();
                var enrolled = new List<int>();

                foreach (var courseId in request.CourseIds)
                {
                    try
                    {
                        await _enrollmentRepo.EnrollStudentAsync(studentId, courseId);
                        enrolled.Add(courseId);
                    }
                    catch (InvalidOperationException ex)
                    {
                        // If one fails (e.g. already enrolled or max reached), continue with others
                        _logger.LogWarning("Enrollment skipped for CourseID {CourseID}: {Message}", courseId, ex.Message);
                        if (ex.Message.Contains("cannot register for more than") || ex.Message.Contains("currently closed"))
                        {
                            return BadRequest(ApiResponse<object>.Fail(ex.Message));
                        }
                    }
                }

                return Ok(ApiResponse<object>.Ok(new { enrolledCourses = enrolled.Count },
                    $"Successfully enrolled in {enrolled.Count} course(s)."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during enrollment.");
                return StatusCode(500, ApiResponse<object>.Fail("Enrollment failed. Please try again."));
            }
        }

        /// <summary>
        /// DELETE /api/courses/enroll/{courseId} — Drop a course.
        /// </summary>
        [HttpDelete("enroll/{courseId}")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> DropCourse(int courseId)
        {
            try
            {
                var studentId = GetUserId();
                var dropped = await _enrollmentRepo.UnenrollStudentAsync(studentId, courseId);

                if (!dropped)
                    return NotFound(ApiResponse<object>.Fail("You are not enrolled in this course."));

                return Ok(ApiResponse<object>.Ok(new { droppedCourseId = courseId }, "Course dropped successfully."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dropping course.");
                return StatusCode(500, ApiResponse<object>.Fail("Failed to drop course."));
            }
        }

        /// <summary>
        /// GET /api/courses/settings — Get registration settings (public).
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

        private int GetUserId()
        {
            var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.Parse(sub!);
        }
    }
}

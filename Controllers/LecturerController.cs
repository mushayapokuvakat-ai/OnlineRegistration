using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineRegistrationSystem.Data;
using OnlineRegistrationSystem.Models;

namespace OnlineRegistrationSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Lecturer")]
    public class LecturerController : ControllerBase
    {
        private readonly CourseRepository _courseRepo;
        private readonly ILogger<LecturerController> _logger;

        public LecturerController(CourseRepository courseRepo, ILogger<LecturerController> logger)
        {
            _courseRepo = courseRepo;
            _logger = logger;
        }

        /// <summary>
        /// GET /api/lecturer/courses — Get courses assigned to this lecturer.
        /// </summary>
        [HttpGet("courses")]
        public async Task<IActionResult> GetMyCourses()
        {
            try
            {
                var lecturerId = GetUserId();
                var courses = await _courseRepo.GetCoursesByLecturerAsync(lecturerId);
                return Ok(ApiResponse<List<Course>>.Ok(courses, $"You have {courses.Count} assigned course(s)."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching lecturer courses.");
                return StatusCode(500, ApiResponse<object>.Fail("Failed to load your courses."));
            }
        }

        /// <summary>
        /// GET /api/lecturer/courses/{id}/students — Get students enrolled in a course.
        /// </summary>
        [HttpGet("courses/{id}/students")]
        public async Task<IActionResult> GetCourseStudents(int id)
        {
            try
            {
                // Verify this lecturer owns the course
                var lecturerId = GetUserId();
                var course = await _courseRepo.GetCourseByIdAsync(id);
                if (course == null || course.LecturerID != lecturerId)
                {
                    return Forbid();
                }

                var students = await _courseRepo.GetEnrolledStudentsAsync(id);
                return Ok(ApiResponse<object>.Ok(new
                {
                    course = new { course.CourseID, course.CourseName, course.CourseCode },
                    students,
                    totalStudents = students.Count
                }, $"{students.Count} student(s) enrolled."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching course students.");
                return StatusCode(500, ApiResponse<object>.Fail("Failed to load students."));
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

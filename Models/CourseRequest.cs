using System.ComponentModel.DataAnnotations;

namespace OnlineRegistrationSystem.Models
{
    /// <summary>
    /// DTO for creating a new course (admin).
    /// </summary>
    public class CreateCourseRequest
    {
        [Required(ErrorMessage = "Course name is required.")]
        [StringLength(100, MinimumLength = 2)]
        public string CourseName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Course code is required.")]
        [StringLength(20, MinimumLength = 2)]
        public string CourseCode { get; set; } = string.Empty;

        [StringLength(500)]
        public string Description { get; set; } = string.Empty;

        public int? LecturerID { get; set; }

        [Range(1, 1000)]
        public int MaxCapacity { get; set; } = 100;
    }

    /// <summary>
    /// DTO for course enrollment (student).
    /// </summary>
    public class EnrollRequest
    {
        [Required(ErrorMessage = "At least one course must be selected.")]
        public List<int> CourseIds { get; set; } = new();
    }

    /// <summary>
    /// DTO for updating registration settings (admin).
    /// </summary>
    public class UpdateSettingsRequest
    {
        public bool RegistrationOpen { get; set; } = true;
        [Range(1, 10)]
        public int MaxCoursesPerStudent { get; set; } = 5;
    }
}

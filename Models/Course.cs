namespace OnlineRegistrationSystem.Models
{
    /// <summary>
    /// Represents a course available for registration.
    /// </summary>
    public class Course
    {
        public int CourseID { get; set; }
        public string CourseName { get; set; } = string.Empty;
        public string CourseCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int? LecturerID { get; set; }
        public string LecturerName { get; set; } = string.Empty;
        public int MaxCapacity { get; set; } = 100;
        public bool IsActive { get; set; } = true;
        public int EnrolledCount { get; set; }
    }
}

using System.ComponentModel.DataAnnotations;

namespace OnlineRegistrationSystem.Models
{
    /// <summary>
    /// DTO for user registration requests.
    /// </summary>
    public class RegisterRequest
    {
        [Required(ErrorMessage = "Full name is required.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 100 characters.")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Invalid email format.")]
        [StringLength(100)]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters.")]
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// Role defaults to "Student". Can be "Student" or "Lecturer".
        /// </summary>
        public string Role { get; set; } = "Student";
    }
}

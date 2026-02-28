using Microsoft.AspNetCore.Mvc;
using OnlineRegistrationSystem.Data;
using OnlineRegistrationSystem.Models;
using OnlineRegistrationSystem.Services;

namespace OnlineRegistrationSystem.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly UserRepository _userRepo;
        private readonly JwtService _jwtService;
        private readonly EmailService _emailService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            UserRepository userRepo,
            JwtService jwtService,
            EmailService emailService,
            ILogger<AuthController> logger)
        {
            _userRepo = userRepo;
            _jwtService = jwtService;
            _emailService = emailService;
            _logger = logger;
        }

        /// <summary>
        /// POST /api/auth/register
        /// Registers a new user.
        /// </summary>
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ApiResponse<object>.Fail("Validation failed. Please check your inputs."));

            try
            {
                // Check for duplicate email
                if (await _userRepo.EmailExistsAsync(request.Email))
                {
                    return Conflict(ApiResponse<object>.Fail("An account with this email already exists."));
                }

                // Validate role
                var role = request.Role;
                if (role != "Student" && role != "Lecturer")
                    role = "Student";

                // Hash password using BCrypt
                var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

                // Insert user
                var userId = await _userRepo.CreateUserAsync(
                    request.FullName,
                    request.Email,
                    passwordHash,
                    "N/A",
                    role
                );

                // Send verification email (stub)
                await _emailService.SendVerificationEmailAsync(request.Email, request.FullName);

                // NOTIFY ADMIN via Email
                var adminEmail = "mushayapokuvakat@africau.edu";
                var adminSubject = "Alert: New Student Registration";
                var adminMessage = $@"
                    <h2>New Student Registration</h2>
                    <p><strong>Name:</strong> {request.FullName}</p>
                    <p><strong>Email:</strong> {request.Email}</p>
                    <p><strong>Role:</strong> {role}</p>
                    <p><strong>Time:</strong> {DateTime.Now}</p>
                ";
                await _emailService.SendEmailAsync(adminEmail, adminSubject, adminMessage);

                _logger.LogInformation("User registered successfully: {Email}, UserID: {UserID}", request.Email, userId);

                return Ok(ApiResponse<object>.Ok(new { userId }, "Registration successful! A verification email has been sent."));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration for {Email}", request.Email);
                return StatusCode(500, ApiResponse<object>.Fail("An internal error occurred. Please try again later."));
            }
        }

        /// <summary>
        /// POST /api/auth/login
        /// Authenticates a user and returns a JWT token.
        /// </summary>
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ApiResponse<object>.Fail("Validation failed."));

            try
            {
                var user = await _userRepo.GetUserByEmailAsync(request.Email);
                if (user == null)
                {
                    return Unauthorized(ApiResponse<object>.Fail("Invalid email or password."));
                }

                // Verify password
                if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                {
                    return Unauthorized(ApiResponse<object>.Fail("Invalid email or password."));
                }

                // Generate JWT token
                var token = _jwtService.GenerateToken(user.UserID, user.Email, user.FullName, user.Role);

                _logger.LogInformation("User logged in: {Email}", user.Email);

                return Ok(ApiResponse<AuthResponse>.Ok(new AuthResponse
                {
                    Token = token,
                    FullName = user.FullName,
                    Email = user.Email,
                    Role = user.Role
                }, "Login successful!"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login for {Email}", request.Email);
                return StatusCode(500, ApiResponse<object>.Fail("An internal error occurred."));
            }
        }
    }
}

using CustomerService.Application.Dtos;

namespace CustomerService.Application.Interfaces;

/// <summary>Application service contract for authentication.</summary>
public interface IAuthService
{
    /// <summary>Validates credentials and returns a JWT on success.</summary>
    /// <param name="request">Login request.</param>
    /// <returns>A <see cref="LoginResponse"/> or null if invalid.</returns>
    Task<LoginResponse?> LoginAsync(LoginRequest request);

    /// <summary>Returns the signed-in staff member's own profile (JWT-scoped).</summary>
    Task<StaffProfileDto> GetProfileAsync(string userId);

    /// <summary>Updates the signed-in staff member's own name (email never touched).</summary>
    Task UpdateProfileAsync(string userId, UpdateStaffProfileDto dto);

    /// <summary>
    /// Generates a password-reset token for the staff member and emails a
    /// reset link via the existing notification/email infrastructure.
    /// </summary>
    Task RequestPasswordResetAsync(string userId);

    /// <summary>
    /// Validates a reset token and sets a new password. Returns true on
    /// success, false if the token is invalid/expired/already used.
    /// </summary>
    Task<bool> ResetPasswordAsync(ResetPasswordRequest request);
}

/// <summary>Application service contract for dashboard analytics.</summary>
public interface IDashboardService
{
    /// <summary>Builds the full dashboard payload.</summary>
    /// <param name="agentId">
    /// When set (Agent caller), every number/chart is scoped to cases assigned
    /// to this user id. When null (Admin caller), the view is company-wide.
    /// </param>
    /// <returns>A <see cref="DashboardDto"/>.</returns>
    Task<DashboardDto> GetDashboardAsync(string? agentId = null);
}

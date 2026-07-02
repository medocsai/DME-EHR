using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Models;
using EHR.Services;
using System.Security.Claims;

namespace EHR.Controllers;

/// <summary>
/// User management controller - Super Admin and Clinic Admin only
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IUserManagementService _userService;

    public UsersController(IUserManagementService userService)
    {
        _userService = userService;
    }

    /// <summary>
    /// Get all users (Super Admin sees all, Clinic Admin sees only their tenant's users)
    /// </summary>
    /// <param name="tenantId">Optional tenant filter for Super Admin</param>
    /// <param name="activeOnly">true = active only, false = inactive only, null = all</param>
    [HttpGet]
    [Authorize(Roles = "0,1")]
    public async Task<ActionResult<List<UserListDto>>> GetUsers([FromQuery] int? tenantId = null, [FromQuery] bool? activeOnly = true)
    {
        var users = await _userService.GetUsersAsync(tenantId, activeOnly);
        return Ok(users);
    }

    /// <summary>
    /// Get a specific user by ID
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(Roles = "0,1")]
    public async Task<ActionResult<UserListDto>> GetUser(int id)
    {
        var user = await _userService.GetUserByIdAsync(id);
        if (user == null)
            return NotFound(new { message = "User not found" });

        return Ok(user);
    }

    /// <summary>
    /// Create a new user
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "0,1")]
    public async Task<ActionResult<UserListDto>> CreateUser([FromBody] UserCreateDto dto)
    {
        try
        {
            var user = await _userService.CreateUserAsync(dto);
            return CreatedAtAction(nameof(GetUser), new { id = user.UserId }, new UserListDto
            {
                UserId = user.UserId,
                TenantId = user.TenantId,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Role = user.Role ?? 0,
                ProviderId = user.ProviderId,
                IsActive = user.IsActive ?? false
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "An error occurred while creating the user", details = ex.Message });
        }
    }

    /// <summary>
    /// Update a user
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "0,1")]
    public async Task<ActionResult> UpdateUser(int id, [FromBody] UserUpdateDto dto)
    {
        try
        {
            var user = await _userService.UpdateUserAsync(id, dto);
            if (user == null)
                return NotFound(new { message = "User not found" });

            return Ok(new { message = "User updated successfully" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Delete (deactivate) a user
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "0,1")]
    public async Task<ActionResult> DeleteUser(int id)
    {
        var result = await _userService.DeleteUserAsync(id);
        if (!result)
            return NotFound(new { message = "User not found" });

        return Ok(new { message = "User deactivated successfully" });
    }

    /// <summary>
    /// Reactivate a deactivated user
    /// </summary>
    [HttpPost("{id}/reactivate")]
    [Authorize(Roles = "0,1")]
    public async Task<ActionResult> ReactivateUser(int id)
    {
        var result = await _userService.ReactivateUserAsync(id);
        if (!result)
            return NotFound(new { message = "User not found" });

        return Ok(new { message = "User reactivated successfully" });
    }

    /// <summary>
    /// Super Admin or Clinic Admin: Reset a user's password
    /// </summary>
    [HttpPost("{id}/reset-password")]
    [Authorize(Roles = "0,1")]
    public async Task<ActionResult> ResetUserPassword(int id, [FromBody] AdminResetPasswordDto dto)
    {
        if (dto.UserId != id)
            return BadRequest(new { message = "User ID mismatch" });

        if (string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword.Length < 8)
            return BadRequest(new { message = "Password must be at least 8 characters" });

        var result = await _userService.AdminResetPasswordAsync(id, dto.NewPassword);
        if (!result)
            return NotFound(new { message = "User not found" });

        return Ok(new { message = "Password reset successfully" });
    }

    /// <summary>
    /// Super Admin or Clinic Admin: Change a user's email
    /// </summary>
    [HttpPost("{id}/change-email")]
    [Authorize(Roles = "0,1")]
    public async Task<ActionResult> ChangeUserEmail(int id, [FromBody] AdminChangeEmailDto dto)
    {
        if (dto.UserId != id)
            return BadRequest(new { message = "User ID mismatch" });

        if (string.IsNullOrWhiteSpace(dto.NewEmail) || !dto.NewEmail.Contains('@'))
            return BadRequest(new { message = "Invalid email address" });

        try
        {
            var result = await _userService.AdminChangeEmailAsync(id, dto.NewEmail);
            if (!result)
                return NotFound(new { message = "User not found" });

            return Ok(new { message = "Email changed successfully" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

using System.Collections.Concurrent;
using ConnectBackend.Data;
using ConnectBackend.Models;
using ConnectBackend.Repositories.Interfaces;
using Dapper;

namespace ConnectBackend.Repositories;

public class UserRepository : IUserRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private static readonly ConcurrentDictionary<string, User> _memoryUsers = new();

    public UserRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<User?> GetByIdAsync(long id)
    {
        try
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = "SELECT TOP 1 * FROM [identity].[Users] WHERE UserId = @Id";
            var user = await conn.QueryFirstOrDefaultAsync<User>(sql, new { Id = id });
            if (user != null) return user;
        }
        catch { }

        return _memoryUsers.Values.FirstOrDefault(u => u.UserId == id);
    }

    public async Task<User?> GetByFirebaseUidAsync(string firebaseUid)
    {
        try
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = "SELECT TOP 1 * FROM [identity].[Users] WHERE FirebaseUid = @FirebaseUid";
            var user = await conn.QueryFirstOrDefaultAsync<User>(sql, new { FirebaseUid = firebaseUid });
            if (user != null) return user;
        }
        catch { }

        return _memoryUsers.Values.FirstOrDefault(u => u.FirebaseUid == firebaseUid);
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        try
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = "SELECT TOP 1 * FROM [identity].[Users] WHERE LOWER(Email) = LOWER(@Email)";
            var user = await conn.QueryFirstOrDefaultAsync<User>(sql, new { Email = email.Trim() });
            if (user != null) return user;
        }
        catch { }

        return _memoryUsers.Values.FirstOrDefault(u => string.Equals(u.Email, email.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public async Task<User> UpsertUserAsync(User user, string? defaultRole = "EV_OWNER")
    {
        try
        {
            using var conn = _connectionFactory.CreateConnection();

            // 1. Check if user already exists by FirebaseUid or Email
            const string selectSql = @"
                SELECT TOP 1 * FROM [identity].[Users] 
                WHERE FirebaseUid = @FirebaseUid OR (Email IS NOT NULL AND LOWER(Email) = LOWER(@Email))";

            var existing = await conn.QueryFirstOrDefaultAsync<User>(selectSql, new 
            { 
                user.FirebaseUid, 
                Email = user.Email ?? "" 
            });

            if (existing != null)
            {
                const string updateSql = @"
                    UPDATE [identity].[Users]
                    SET FirstName = COALESCE(@FirstName, FirstName),
                        LastName = COALESCE(@LastName, LastName),
                        ProfileImageUrl = COALESCE(@ProfileImageUrl, ProfileImageUrl),
                        MobileNumber = COALESCE(@MobileNumber, MobileNumber),
                        LastLoginAt = SYSUTCDATETIME(),
                        UpdatedAt = SYSUTCDATETIME()
                    OUTPUT INSERTED.*
                    WHERE UserId = @UserId";

                return await conn.QuerySingleAsync<User>(updateSql, new
                {
                    existing.UserId,
                    user.FirstName,
                    user.LastName,
                    user.ProfileImageUrl,
                    user.MobileNumber
                });
            }

            // 2. Insert new user
            if (string.IsNullOrWhiteSpace(user.UserCode))
            {
                user.UserCode = "USR-" + Guid.NewGuid().ToString("N")[..8].ToUpper();
            }

            const string insertSql = @"
                INSERT INTO [identity].[Users]
                    (FirebaseUid, UserCode, FirstName, LastName, Email, MobileNumber, ProfileImageUrl, Status, IsEmailVerified, IsMobileVerified, LastLoginAt, CreatedAt, UpdatedAt)
                OUTPUT INSERTED.*
                VALUES
                    (@FirebaseUid, @UserCode, @FirstName, @LastName, @Email, @MobileNumber, @ProfileImageUrl, 'ACTIVE', 1, 0, SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME())";

            var createdUser = await conn.QuerySingleAsync<User>(insertSql, user);

            // 3. Assign Default Role in [identity].[UserRoles]
            const string assignRoleSql = @"
                DECLARE @RoleId INT = (SELECT TOP 1 RoleId FROM [identity].[Roles] WHERE RoleCode = @RoleCode);
                IF @RoleId IS NOT NULL
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM [identity].[UserRoles] WHERE UserId = @UserId AND RoleId = @RoleId)
                    BEGIN
                        INSERT INTO [identity].[UserRoles] (UserId, RoleId, AssignedAt)
                        VALUES (@UserId, @RoleId, SYSUTCDATETIME());
                    END
                END";

            await conn.ExecuteAsync(assignRoleSql, new 
            { 
                UserId = createdUser.UserId, 
                RoleCode = defaultRole ?? "EV_OWNER" 
            });

            return createdUser;
        }
        catch
        {
            // Fallback for cloud demo mode
            var existing = _memoryUsers.Values.FirstOrDefault(u => 
                (!string.IsNullOrEmpty(user.FirebaseUid) && u.FirebaseUid == user.FirebaseUid) ||
                (!string.IsNullOrEmpty(user.Email) && string.Equals(u.Email, user.Email, StringComparison.OrdinalIgnoreCase)));

            if (existing != null)
            {
                existing.FirstName = user.FirstName ?? existing.FirstName;
                existing.LastName = user.LastName ?? existing.LastName;
                existing.ProfileImageUrl = user.ProfileImageUrl ?? existing.ProfileImageUrl;
                existing.LastLoginAt = DateTime.UtcNow;
                return existing;
            }

            user.UserId = _memoryUsers.Count + 1001;
            user.UserCode ??= "USR-" + Guid.NewGuid().ToString("N")[..8].ToUpper();
            user.Status = "ACTIVE";
            user.CreatedAt = DateTime.UtcNow;
            user.LastLoginAt = DateTime.UtcNow;

            _memoryUsers[user.FirebaseUid ?? user.UserId.ToString()] = user;
            return user;
        }
    }

    public async Task UpdateLastLoginAsync(long userId)
    {
        try
        {
            using var conn = _connectionFactory.CreateConnection();
            const string sql = "UPDATE [identity].[Users] SET LastLoginAt = SYSUTCDATETIME(), UpdatedAt = SYSUTCDATETIME() WHERE UserId = @UserId";
            await conn.ExecuteAsync(sql, new { UserId = userId });
        }
        catch { }
    }
}

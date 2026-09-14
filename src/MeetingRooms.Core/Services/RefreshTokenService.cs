using System.Security.Cryptography;
using MeetingRooms.Core.Abstractions;
using MeetingRooms.Core.Entities;
using MeetingRooms.Core.Options;
using MeetingRooms.Core.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MeetingRooms.Core.Services;

/// <summary>
/// Issues, rotates and revokes refresh tokens.
/// </summary>
/// <remarks>
/// The raw token never touches the database — only a SHA-256 hash of it does. A plain hash without
/// a salt is the right choice here, unlike for passwords: the token is 256 bits of cryptographic
/// randomness, so there is no dictionary to attack and nothing for a salt to defend against.
/// </remarks>
/// <param name="unitOfWork">Token storage and the commit point.</param>
/// <param name="options">Token lifetime.</param>
/// <param name="timeProvider">Supplies the current time, so expiry is testable.</param>
public sealed class RefreshTokenService(
    IUnitOfWork unitOfWork,
    IOptions<RefreshTokenOptions> options,
    TimeProvider timeProvider) : IRefreshTokenService
{
    /// <summary>Token size in bytes. 256 bits of entropy makes guessing hopeless.</summary>
    private const int TokenBytes = 32;

    private readonly IUnitOfWork unitOfWork = unitOfWork;
    private readonly RefreshTokenOptions options = options.Value;
    private readonly TimeProvider timeProvider = timeProvider;

    /// <inheritdoc />
    public async Task<IssuedRefreshToken> IssueAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var issued = this.CreateToken(userId);

        this.unitOfWork.RefreshTokens.Add(issued.Entity);
        await this.unitOfWork.SaveChangesAsync(cancellationToken);

        return new IssuedRefreshToken(issued.RawToken, issued.Entity.ExpiresUtc);
    }

    /// <inheritdoc />
    public async Task<OperationResult<RotatedRefreshToken>> RotateAsync(
        string rawToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return OperationResult<RotatedRefreshToken>.Forbidden("No refresh token was supplied.");
        }

        var hash = Hash(rawToken);

        var existing = await this.unitOfWork.RefreshTokens
            .Query()
            .FirstOrDefaultAsync(token => token.TokenHash == hash, cancellationToken);

        var now = this.timeProvider.GetUtcNow();

        if (existing is null || !existing.IsRedeemableAt(now))
        {
            // Deliberately one message for "unknown", "expired" and "already used": telling a caller
            // which of those applies would help someone probing with stolen tokens.
            return OperationResult<RotatedRefreshToken>.Forbidden(
                "That session has expired. Please sign in again.");
        }

        existing.RevokedUtc = now;

        var replacement = this.CreateToken(existing.UserId);
        this.unitOfWork.RefreshTokens.Add(replacement.Entity);

        try
        {
            await this.unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Redeeming is a read-then-write, so two requests presenting the same cookie can both
            // pass the check above. The rowversion makes the second save fail, and it must fail
            // closed: allowing it would issue two replacements for one token, which is precisely
            // the replay this design exists to detect. Same message as an unknown token, so a
            // caller learns nothing from which branch it took.
            return OperationResult<RotatedRefreshToken>.Forbidden(
                "That session has expired. Please sign in again.");
        }

        return OperationResult<RotatedRefreshToken>.Success(
            new RotatedRefreshToken(existing.UserId, replacement.RawToken, replacement.Entity.ExpiresUtc));
    }

    /// <inheritdoc />
    public async Task RevokeAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return;
        }

        var hash = Hash(rawToken);

        var existing = await this.unitOfWork.RefreshTokens
            .Query()
            .FirstOrDefaultAsync(token => token.TokenHash == hash, cancellationToken);

        if (existing is null || existing.RevokedUtc is not null)
        {
            // Signing out with a token that is already dead is not an error worth reporting.
            return;
        }

        existing.RevokedUtc = this.timeProvider.GetUtcNow();

        try
        {
            await this.unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Somebody else revoked or rotated it first. The token is dead either way, which is all
            // signing out asked for.
        }
    }

    /// <summary>Hashes a raw token for storage and lookup.</summary>
    /// <param name="rawToken">The raw token value.</param>
    /// <returns>The hex-encoded SHA-256 hash.</returns>
    private static string Hash(string rawToken) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));

    /// <summary>Generates a token and its unsaved entity.</summary>
    /// <param name="userId">The user the token belongs to.</param>
    /// <returns>The raw token and the row that records its hash.</returns>
    private (string RawToken, RefreshToken Entity) CreateToken(Guid userId)
    {
        // Base64Url so the value is safe in a cookie without further encoding.
        var rawToken = Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));
        var now = this.timeProvider.GetUtcNow();

        var entity = new RefreshToken
        {
            TokenHash = Hash(rawToken),
            UserId = userId,
            CreatedUtc = now,
            ExpiresUtc = now.AddDays(this.options.LifetimeDays),
        };

        return (rawToken, entity);
    }

    /// <summary>Encodes bytes using the URL- and cookie-safe base64 alphabet.</summary>
    /// <param name="bytes">The bytes to encode.</param>
    /// <returns>The encoded string, without padding.</returns>
    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

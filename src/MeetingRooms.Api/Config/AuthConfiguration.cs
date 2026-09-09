using System.Text;
using MeetingRooms.Api.Auth;
using MeetingRooms.Api.Hubs;
using MeetingRooms.Core.Entities;
using MeetingRooms.Infrastructure.SQL.Database;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace MeetingRooms.Api.Config;

/// <summary>Configures ASP.NET Core Identity, JWT bearer authentication and authorization.</summary>
public static class AuthConfiguration
{
    /// <summary>
    /// Adds Identity with <see cref="Guid"/> keys, JWT validation and role-based authorization.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static WebApplicationBuilder AddAuth(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddIdentity();
        builder.AddJwtAuthentication();

        builder.Services.AddAuthorization();

        return builder;
    }

    /// <summary>Registers the Identity stores and password rules.</summary>
    /// <param name="builder">The host application builder.</param>
    private static void AddIdentity(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                // Length does more for password strength than character-class rules, which mostly
                // produce predictable substitutions.
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AppDbContext>();
    }

    /// <summary>Configures bearer-token validation.</summary>
    /// <param name="builder">The host application builder.</param>
    private static void AddJwtAuthentication(this WebApplicationBuilder builder)
    {
        var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException(
                $"The '{JwtOptions.SectionName}' configuration section is missing. Set it in user " +
                "secrets locally, or in the Web App's application settings in Azure.");

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Without this, the handler rewrites short claim names into long schemas.xmlsoap.org
                // URIs, so "sub" would not be readable as "sub". Off means what the token says is
                // what the code sees.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                    ValidateLifetime = true,

                    // Small but non-zero, so a slight clock difference between the Web App and the
                    // token issuer does not reject freshly issued tokens.
                    ClockSkew = TimeSpan.FromSeconds(30),

                    // Must match the claim names the token service writes.
                    NameClaimType = ClaimNames.Name,
                    RoleClaimType = ClaimNames.Role,
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // A browser WebSocket cannot set an Authorization header, so the SignalR
                        // JavaScript client appends the token as ?access_token=... instead. Without
                        // this hook the hub rejects every connection despite the client being
                        // signed in. Restricted to the hub path so a token in a query string is
                        // never accepted for ordinary API calls, where it could end up in logs.
                        var accessToken = context.Request.Query["access_token"];

                        if (!string.IsNullOrEmpty(accessToken)
                            && context.HttpContext.Request.Path.StartsWithSegments(BookingHub.Route))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },
                };
            });
    }
}

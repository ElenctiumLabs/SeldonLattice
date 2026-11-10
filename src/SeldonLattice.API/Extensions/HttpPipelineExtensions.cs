using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using SeldonLattice.API.Authentication;
using SeldonLattice.API.Middleware;
using SeldonLattice.Domain.Interfaces;

namespace SeldonLattice.API.Extensions;

/// <summary>
/// HTTP surface services (CORS policy, API-key authentication, authorization policies,
/// JSON serialization, Swagger document) and the middleware pipeline that consumes them.
/// </summary>
public static class HttpPipelineExtensions
{
    public static void AddSeldonLatticeHttpServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        // CORS configuration - read allowed origins from configuration
        var corsConfig = configuration.GetSection("Cors:FrontendPolicy");
        var corsOrigins = corsConfig.GetSection("AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options =>
        {
            options.AddPolicy("Frontend", policy =>
            {
                policy.WithOrigins(corsOrigins)
                    .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
                    .WithHeaders("Content-Type", "Authorization", "X-Request-Id", "X-Admin-Key")
                    .AllowCredentials();
            });
        });

        // MVC controllers (Health, Auth, Metrics, ...) - the endpoint surface being
        // migrated off minimal-API route groups one group at a time.
        services.AddControllers();

        // Authentication & Authorization (API key-based)
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = ApiKeyAuthOptions.DefaultScheme;
                options.DefaultChallengeScheme = ApiKeyAuthOptions.DefaultScheme;
            })
            .AddScheme<ApiKeyAuthOptions, ApiKeyAuthHandler>(
                ApiKeyAuthOptions.DefaultScheme, _ => { });

        services.AddAuthorizationBuilder()
            // Granular, non-overlapping authorization policies: each policy requires
            // exactly one role; no role implicitly satisfies another role's policy
            // (see docs/audits/api-hardening-2026-10-20.md).
            .AddPolicy("AdminOnly", policy => policy.RequireRole("admin"))
            .AddPolicy("ReaderOnly", policy => policy.RequireRole("reader"))
            .AddPolicy("AscentOnly", policy => policy.RequireRole("dialectician"))
            .AddPolicy("IngestOnly", policy => policy.RequireRole("ingest"))
            .AddPolicy("SettingsOnly", policy => policy.RequireRole("settings"))
            .AddPolicy("WriterOnly", policy => policy.RequireRole("writer"))
            .AddPolicy("StreamOnly", policy => policy.AddRequirements(new StreamOnlyRequirement()))
            .AddPolicy("AnyAuthenticated", policy => policy.RequireAuthenticatedUser());

        // Rate limiting (decision D5): the ASP.NET built-in RateLimiter replaces the
        // hand-rolled RateLimitMiddleware entirely (deleted - it was never wired into
        // the pipeline, finding F6). Token bucket per API key (IP fallback for
        // anonymous callers): sustained RequestsPerMinute with BurstLimit capacity.
        var rateSection = configuration.GetSection("RateLimiting");
        var requestsPerMinute = rateSection.GetValue("RequestsPerMinute", 30);
        var burstLimit = rateSection.GetValue("BurstLimit", 10);
        var windowSeconds = rateSection.GetValue("WindowSizeSeconds", 60);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // GlobalLimiter rather than a named policy: applied to every request
            // (middleware runs the global limiter first; endpoints opt out with
            // [DisableRateLimiting], which short-circuits before it - verified
            // against RateLimitingMiddleware). No [EnableRateLimiting] needed
            // anywhere, so a future controller can't accidentally skip the limit.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var apiKey = httpContext.User.FindFirst("api_key")?.Value;
                var partitionKey = string.IsNullOrEmpty(apiKey)
                    ? $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}"
                    : $"key:{apiKey}";

                return RateLimitPartition.GetTokenBucketLimiter(partitionKey, _ =>
                    new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = burstLimit,
                        TokensPerPeriod = requestsPerMinute,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(windowSeconds),
                        AutoReplenishment = true,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    });
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.RequestServices
                    .GetRequiredService<SeldonLatticeMetrics>()
                    .RecordRateLimitRejection();
                context.HttpContext.Response.Headers.RetryAfter =
                    Math.Max(1, windowSeconds / 60).ToString();

                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    error = "rate_limit_exceeded",
                    message = "Too many requests. Please retry after the specified interval.",
                    limit = requestsPerMinute,
                    windowSeconds
                }, cancellationToken);
            };
        });

        // Transport hardening (findings F8/F9): HSTS registration for non-dev
        // pipelines, and a ProblemDetails-based 5xx contract.
        services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(365);
            options.IncludeSubDomains = true;
        });
        services.AddProblemDetails();

        services.AddSingleton<IApiKeyValidator, ApiKeyValidator>();
        services.Configure<StreamTokenOptions>(configuration.GetSection("StreamTokens"));
        services.AddSingleton<StreamTokenService>();
        services.AddHttpContextAccessor();
        services.AddSingleton<IAuthorizationHandler, StreamOnlyHandler>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // Configure JSON serialization options for camelCase property naming
        services.Configure<JsonSerializerOptions>(options =>
        {
            options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });

        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Seldon Lattice API",
                Version = "v1",
                Description = "Auditable AI reasoning with cryptographic proof and multi-agent dialectical synthesis. " +
                              "Seldon Lattice applies philosophy-as-code - the dialectical method of Thesis, Antithesis, " +
                              "Synthesis - to enterprise decision support. Every reasoning step is recorded in a Merkle DAG " +
                              "with SHA3-256 hashing and ML-DSA-65 post-quantum signatures for tamper evidence.",
                Contact = new OpenApiContact
                {
                    Name = "Seldon Lattice",
                    Url = new Uri("https://seldonlattice.com/contact"),
                },
                License = new OpenApiLicense
                {
                    Name = "Proprietary",
                    Url = new Uri("https://seldonlattice.com/terms"),
                },
            });
            c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Description = "API key-based authentication. Enter as 'Bearer {key}' or 'ApiKey {key}'.",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "apiKey",
            });
            // Microsoft.OpenApi 2.x replaced the single OpenApiReference type with one reference
            // type per target (OpenApiSecuritySchemeReference here), and AddSecurityRequirement now
            // takes a factory so the reference can be bound to the host document it belongs to.
            c.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecuritySchemeReference("ApiKey"),
                    []
                }
            });
        });
    }

    extension(WebApplication app)
    {
        public void ConfigureSeldonLatticePipeline()
        {
            if (!app.Environment.IsDevelopment())
            {
                // Global 5xx contract (finding F9): application/problem+json with no
                // stack traces or exception details outside development.
                app.UseExceptionHandler(exceptionHandlerApp =>
                {
                    exceptionHandlerApp.Run(async context =>
                    {
                        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
                        context.RequestServices.GetRequiredService<ILoggerFactory>()
                            .CreateLogger("SeldonLattice.API.GlobalExceptionHandler")
                            .LogError(exception, "Unhandled exception for {Method} {Path}",
                                context.Request.Method, context.Request.Path);

                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        context.Response.ContentType = "application/problem+json";
                        await context.Response.WriteAsJsonAsync(new ProblemDetails
                        {
                            Status = StatusCodes.Status500InternalServerError,
                            Title = "An internal server error occurred.",
                            Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                        });
                    });
                });

                app.UseHsts(); // finding F8: strict-transport-security in non-dev
                app.UseHttpsRedirection();
            }

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger(c => { c.RouteTemplate = "swagger/{documentName}/swagger.json"; });
                app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Seldon Lattice API v1");
                    c.RoutePrefix = "swagger";
                });
            }

            app.UseSecurityHeaders(); // finding F8: nosniff / frame-deny / no-referrer

            app.UseCors("Frontend"); // Apply CORS before endpoint mapping

            app.UseObservability(); // Correlation ID, metrics, distributed tracing

            app.UseAuthentication(); // API key authentication
            app.UseRateLimiter(); // D5: after auth so partitions key on the api_key claim
            app.UseAuthorization(); // Role-based authorization
        }

        /// <summary>
        /// Fail fast outside Development when the host has no custom stream-token signing secret.
        /// The committed development default would let anyone forge SSE tokens in production.
        /// </summary>
        public void EnsureStreamTokenSecretConfigured()
        {
            if (app.Environment.IsDevelopment()) return;

            var streamSecret = app.Configuration["StreamTokens:Secret"];
            if (string.IsNullOrWhiteSpace(streamSecret) || streamSecret == "dev-stream-token-secret")
            {
                throw new InvalidOperationException(
                    "StreamTokens:Secret is missing or still the committed dev default. " +
                    "Set the 'StreamTokens:Secret' configuration key (e.g. environment variable " +
                    "StreamTokens__Secret) to a unique random value before running outside development.");
            }
        }
    }

    /// <summary>
    /// Baseline security headers on every response (finding F8): no MIME sniffing,
    /// no framing, no referrer leakage.
    /// </summary>
    private static void UseSecurityHeaders(this IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["X-Frame-Options"] = "DENY";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                return Task.CompletedTask;
            });

            await next(context);
        });
    }
}

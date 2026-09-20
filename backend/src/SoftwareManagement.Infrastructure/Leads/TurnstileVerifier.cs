using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SoftwareManagement.Application.Common;
using SoftwareManagement.Application.Leads;
using SoftwareManagement.Domain.Common;

namespace SoftwareManagement.Infrastructure.Leads;

/// <summary>
/// Verifies a Cloudflare Turnstile token, server-side, once.
///
/// Two rules matter and both are about replay. A token is spent the first time it is presented, and
/// a token older than its lifetime is refused whether or not it was spent (BR-LEAD-02). The spent
/// tokens are held in memory with exactly the token's own lifetime as their expiry, so the record
/// cannot outlive the thing it protects and cannot grow without bound.
///
/// When no secret is configured the verifier fails closed for real tokens and accepts only the
/// explicitly configured development bypass. Failing open would mean a misconfigured deployment
/// silently has no bot protection at all, which is the state you least want to be in and least
/// likely to notice.
/// </summary>
public sealed partial class TurnstileVerifier(
    HttpClient httpClient,
    IMemoryCache spentTokens,
    IConfiguration configuration,
    IClock clock,
    ILogger<TurnstileVerifier> logger) : ICaptchaVerifier
{
    private const string SiteVerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    private readonly HttpClient _httpClient = httpClient;
    private readonly IMemoryCache _spentTokens = spentTokens;
    private readonly IConfiguration _configuration = configuration;
    private readonly IClock _clock = clock;
    private readonly ILogger<TurnstileVerifier> _logger = logger;

    public async Task<CaptchaVerification> VerifyAsync(string? token, string ipAddress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return CaptchaVerification.Rejected("missing");
        }

        // A token is good once. This check comes before anything else, so a replay costs nothing and
        // never reaches Cloudflare (BR-LEAD-02).
        var cacheKey = "captcha:" + token;
        if (_spentTokens.TryGetValue(cacheKey, out _))
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                var maskedIp = PrivacyMask.IpAddress(ipAddress);
                LogTokenReplayed(_logger, maskedIp);
            }
            return CaptchaVerification.Rejected("replayed");
        }

        var bypass = _configuration["Captcha:BypassToken"];
        var secret = _configuration["Captcha:SecretKey"];

        // The bypass exists so the phase gate and the integration tests can post a form without a
        // browser. It is only ever active when an operator has set the value themselves, so a
        // deployment that has not set it has no bypass at all.
        //
        // It is a prefix rather than an exact value, so a caller can present a distinct token each
        // time: "bypass-1", "bypass-2". That matters because the spend-once rule above applies to
        // bypass tokens too. If the bypass were a single fixed string, the first submission would
        // spend it and every later one would be refused as a replay, and the tests could not tell a
        // working replay check from a broken form.
        if (!string.IsNullOrEmpty(bypass) && token.StartsWith(bypass, StringComparison.Ordinal))
        {
            Spend(cacheKey);
            return CaptchaVerification.Ok();
        }

        if (string.IsNullOrWhiteSpace(secret))
        {
            LogNoSecretConfigured(_logger);
            return CaptchaVerification.Rejected("not-configured");
        }

        SiteVerifyResponse? verdict;

        try
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["secret"] = secret,
                ["response"] = token,
                ["remoteip"] = ipAddress,

                // Cloudflare uses this to make a retried verification idempotent, so a network
                // timeout followed by a retry does not spend the token twice on their side.
                ["idempotency_key"] = Guid.NewGuid().ToString("N"),
            });

            using var response = await _httpClient.PostAsync(new Uri(SiteVerifyUrl), form, cancellationToken).ConfigureAwait(false);
            verdict = await response.Content.ReadFromJsonAsync<SiteVerifyResponse>(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            // Cloudflare being unreachable must not become an open door.
            LogVerificationUnreachable(_logger, exception);
            return CaptchaVerification.Rejected("unreachable");
        }
        catch (TaskCanceledException exception)
        {
            LogVerificationUnreachable(_logger, exception);
            return CaptchaVerification.Rejected("timeout");
        }

        if (verdict is null || !verdict.Success)
        {
            var reason = verdict?.ErrorCodes is { Length: > 0 } codes ? string.Join(',', codes) : "rejected";
            return CaptchaVerification.Rejected(reason);
        }

        if (verdict.ChallengeTimestamp is { } issuedAt)
        {
            var age = _clock.UtcNow - issuedAt.ToUniversalTime();
            if (age.TotalSeconds > ICaptchaVerifier.MaxTokenAgeSeconds)
            {
                return CaptchaVerification.Rejected(
                    "expired:" + age.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture) + "s");
            }
        }

        Spend(cacheKey);
        return CaptchaVerification.Ok();
    }

    private void Spend(string cacheKey) =>
        _spentTokens.Set(cacheKey, true, TimeSpan.FromSeconds(ICaptchaVerifier.MaxTokenAgeSeconds));

    private sealed record SiteVerifyResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("challenge_ts")] DateTimeOffset? ChallengeTimestamp,
        [property: JsonPropertyName("error-codes")] string[]? ErrorCodes);

    [LoggerMessage(Level = LogLevel.Information, Message = "A captcha token was presented twice from {ipAddress}; the second submission was refused.")]
    private static partial void LogTokenReplayed(ILogger logger, string ipAddress);

    [LoggerMessage(Level = LogLevel.Error, Message = "No captcha secret is configured, so every public submission is being refused. Set Captcha:SecretKey.")]
    private static partial void LogNoSecretConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The captcha service could not be reached. Submissions are refused while that is true.")]
    private static partial void LogVerificationUnreachable(ILogger logger, Exception exception);
}

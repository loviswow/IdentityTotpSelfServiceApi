namespace IdentityTotpSelfServiceApi.Dtos;

public record RegisterRequest(string Email, string Password);
public record LoginRequest(string Email, string Password);
public record TwoFactorRequest(string ChallengeToken, string Code);
public record RecoveryLoginRequest(string ChallengeToken, string RecoveryCode);
public record TotpCodeRequest(string Code);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record Disable2FaRequest(string Password, string Code);
public record Reset2FaRequest(string Password, string Code);

public record LoginResponse(
    bool RequiresTwoFactor,
    string? AccessToken,
    DateTimeOffset? ExpiresAt,
    string? ChallengeToken);

public record TwoFactorSetupResponse(
    string SharedKey,
    string AuthenticatorUri,
    bool IsTwoFactorEnabled);

public record TwoFactorStatusResponse(
    bool IsTwoFactorEnabled,
    int RecoveryCodesLeft);

namespace IdentityTotpSelfServiceApi.Dtos;

public record RegisterRequest(string Email, string Password);
// DeviceName: 세션 목록에 보일 기기 이름(선택, 최대 128자). 2FA 사용자는 /2fa 또는 /2fa/recovery에 보낸다.
public record LoginRequest(string Email, string Password, string? DeviceName = null);
public record TwoFactorRequest(string ChallengeToken, string Code, string? DeviceName = null);
public record RecoveryLoginRequest(string ChallengeToken, string RecoveryCode, string? DeviceName = null);
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

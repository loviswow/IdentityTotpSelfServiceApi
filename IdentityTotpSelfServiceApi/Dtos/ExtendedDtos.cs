namespace IdentityTotpSelfServiceApi.Dtos;
public record RefreshRequest(string RefreshToken);
public record RevokeRequest(string RefreshToken);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Email, string Token, string NewPassword);
public record ConfirmEmailRequest(string UserId, string Token);
public record ResendConfirmationRequest(string Email);
public record AdminReset2FaRequest(string UserId, string Reason);
public record TokenPairResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, DateTimeOffset RefreshTokenExpiresAt);

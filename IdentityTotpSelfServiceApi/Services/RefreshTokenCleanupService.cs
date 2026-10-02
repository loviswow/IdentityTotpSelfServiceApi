namespace IdentityTotpSelfServiceApi.Services;

// 만료된 Refresh Token 정리 작업. 설정(RefreshTokens:Cleanup:*)
//   Enabled         기본 true
//   IntervalMinutes 실행 주기, 기본 60
//   RetentionDays   만료 후 보존 기간, 기본 30. 세션(family)의 모든 토큰이 이 기간을 넘겨 만료된 경우에만 지운다.
//                   살아 있는 세션의 회전된 토큰을 지우면 재사용 탐지가 동작하지 않기 때문이다.
//   BatchSize       한 번에 지우는 행 수, 기본 500 (긴 잠금 방지)
// 여러 인스턴스에서 동시에 돌면 같은 행을 지우다 동시성 충돌이 날 수 있다. PurgeExpiredAsync가 충돌한 배치를 다시 조회해 계속한다.
public sealed class RefreshTokenCleanupService(IServiceScopeFactory scopes, IConfiguration config, ILogger<RefreshTokenCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("RefreshTokens:Cleanup:Enabled", true)) return;
        var interval = TimeSpan.FromMinutes(Math.Max(1, config.GetValue("RefreshTokens:Cleanup:IntervalMinutes", 60)));
        // 기동 직후 DB 부하가 겹치지 않도록 첫 실행을 조금 늦춘다.
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }
        using var timer = new PeriodicTimer(interval);
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogError(ex, "Refresh token cleanup failed"); }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var retention = Math.Max(0, config.GetValue("RefreshTokens:Cleanup:RetentionDays", 30));
        var batch = Math.Clamp(config.GetValue("RefreshTokens:Cleanup:BatchSize", 500), 1, 10000);
        using var scope = scopes.CreateScope();
        var deleted = await scope.ServiceProvider.GetRequiredService<RefreshTokenService>()
            .PurgeExpiredAsync(DateTimeOffset.UtcNow.AddDays(-retention), batch, ct);
        if (deleted > 0) logger.LogInformation("Deleted {Count} refresh tokens expired more than {Days} days ago", deleted, retention);
        return deleted;
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); } catch (OperationCanceledException) { return false; }
    }
}

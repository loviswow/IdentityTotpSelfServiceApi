using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
namespace IdentityTotpSelfServiceApi.Tests;
// 경쟁 조건을 확정적으로 재현하기 위한 SaveChanges 직전 훅. ApiFactory의 DbContext에 항상 등록되며 Hook이 null이면 아무것도 하지 않는다.
// 훅은 테스트가 정한 조건(특정 사용자 등)에서만 동작하도록 직접 걸러야 한다(같은 factory를 쓰는 테스트가 병렬로 돈다).
public sealed class BeforeSaveHook:SaveChangesInterceptor
{
 public static Func<DbContext,Task>? Hook;
 public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e,InterceptionResult<int> result,CancellationToken ct=default){if(Hook is {} h&&e.Context is not null)await h(e.Context);return result;}
}

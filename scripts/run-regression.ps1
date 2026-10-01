param([string]$ConnectionString="")
$ErrorActionPreference="Stop"
if($ConnectionString){$env:TEST_SQLSERVER_CONNECTION=$ConnectionString}
dotnet restore .\IdentityTotpSelfServiceApi.sln
dotnet build .\IdentityTotpSelfServiceApi.sln -c Release --no-restore
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
dotnet test .\IdentityTotpSelfServiceApi.sln -c Release --no-build --logger "trx;LogFileName=regression.trx" --results-directory .\TestResults
exit $LASTEXITCODE

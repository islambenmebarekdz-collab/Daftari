<#
    يقيس تغطية الاختبارات لطبقة المنطق ويرفض التراجع عنها.

    Vault.cs وNoteCrypto.cs هما كل منطق البيانات والتشفير في التطبيق، والواجهة فوقهما رقيقة.
    قياس التغطية يحسم سؤال «هل هذا مُختبَر؟» بالرقم بدل الرأي — وأي انخفاض عن العتبة
    يعني أن منطقاً جديداً دخل بلا فحص، فيسقط البناء قبل أن يصل إلى المستخدم.
#>
[CmdletBinding()]
param(
    [double]$Minimum = 95.0,
    [string[]]$Files = @('Vault.cs', 'NoteCrypto.cs')
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$results = Join-Path ([System.IO.Path]::GetTempPath()) ("DaftariCoverage_" + [guid]::NewGuid().ToString('N').Substring(0, 8))

Write-Host "قياس التغطية..." -ForegroundColor Cyan
dotnet test (Join-Path $repo 'Daftari.sln') -c Release --nologo `
    --collect:"XPlat Code Coverage" --results-directory $results
if ($LASTEXITCODE -ne 0) { throw "فشلت الاختبارات — لا معنى لقياس التغطية قبل نجاحها." }

$report = Get-ChildItem -Path $results -Filter 'coverage.cobertura.xml' -Recurse | Select-Object -First 1
if (-not $report) { throw "لم يُنتَج تقرير تغطية. تأكد من حزمة coverlet.collector في مشروع الاختبارات." }

[xml]$xml = Get-Content -LiteralPath $report.FullName
$failed = $false

foreach ($name in $Files) {
    $covered = 0
    $total = 0
    foreach ($class in $xml.SelectNodes('//class')) {
        if ([System.IO.Path]::GetFileName($class.filename) -ne $name) { continue }
        foreach ($line in $class.SelectNodes('.//line')) {
            $total++
            if ([int]$line.hits -gt 0) { $covered++ }
        }
    }

    if ($total -eq 0) {
        Write-Host "  $name : لا بيانات تغطية!" -ForegroundColor Red
        $failed = $true
        continue
    }

    $pct = [math]::Round(100.0 * $covered / $total, 1)
    if ($pct -lt $Minimum) {
        Write-Host ("  {0,-16} {1,5}%  ({2}/{3})  دون العتبة {4}%" -f $name, $pct, $covered, $total, $Minimum) -ForegroundColor Red
        $failed = $true
    }
    else {
        Write-Host ("  {0,-16} {1,5}%  ({2}/{3})" -f $name, $pct, $covered, $total) -ForegroundColor Green
    }
}

Remove-Item -LiteralPath $results -Recurse -Force -ErrorAction SilentlyContinue

if ($failed) {
    Write-Host "`nتغطية طبقة المنطق تراجعت. أضف فحوصاً للمنطق الجديد قبل الدمج." -ForegroundColor Red
    exit 1
}
Write-Host "`nتغطية طبقة المنطق سليمة." -ForegroundColor Green

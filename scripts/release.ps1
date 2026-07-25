# سكربت النشر: خطوة واحدة تمنع الخطأ البشري في الترتيب.
# يبني، يشغّل كل الاختبارات (ويتوقف إن فشل واحد)، ثم ينشر النسخة المستقلة
# إلى dist\publish — وهي النسخة التي يشير إليها اختصار سطح المكتب وقائمة ابدأ —
# ويجهّز حزمة zip للإصدار.
#
#   .\scripts\release.ps1              بناء واختبار ونشر محلي
#   .\scripts\release.ps1 -Zip         مع تجهيز حزمة الإصدار
#   .\scripts\release.ps1 -Zip -Publish   ومع إنشاء إصدار GitHub (يتطلب gh)

[CmdletBinding()]
param(
    [switch]$Zip,
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Step($message) { Write-Host "`n=== $message ===" -ForegroundColor Cyan }

# رقم الإصدار من ملف المشروع مصدرَ الحقيقة الوحيد
[xml]$proj = Get-Content (Join-Path $root 'Daftari.csproj')
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'تعذّر قراءة رقم الإصدار من Daftari.csproj' }
Write-Host "الإصدار: $version"

Step 'إغلاق أي نسخة عاملة'
Get-Process Daftari -ErrorAction SilentlyContinue | ForEach-Object {
    $_.CloseMainWindow() | Out-Null
    Start-Sleep -Seconds 2
    if (-not $_.HasExited) { $_.Kill() }
}

Step 'بناء الحل'
dotnet build Daftari.sln -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'فشل البناء' }

Step 'تشغيل الاختبارات'
dotnet test Daftari.sln -c Release --no-build --nologo
if ($LASTEXITCODE -ne 0) { throw 'فشلت الاختبارات — أُوقف النشر' }

Step 'نشر النسخة المستقلة إلى dist\publish'
dotnet publish Daftari.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -o dist\publish --nologo
if ($LASTEXITCODE -ne 0) { throw 'فشل النشر' }
Remove-Item dist\publish\Daftari.pdb -Force -ErrorAction SilentlyContinue

$exe = Join-Path $root 'dist\publish\Daftari.exe'
$built = (Get-Item $exe).VersionInfo.FileVersion
Write-Host "نسخة الملف التنفيذي: $built"
if (-not $built.StartsWith($version)) { throw "عدم تطابق: المشروع $version والملف $built" }

if ($Zip -or $Publish) {
    Step 'تجهيز حزمة الإصدار'
    $zipPath = Join-Path $root "dist\Daftari-$version.zip"
    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path $exe, (Join-Path $root 'dist\publish\اقرأني.txt') -DestinationPath $zipPath -Force
    Write-Host "الحزمة: $zipPath ($([math]::Round((Get-Item $zipPath).Length/1MB,1)) ميغابايت)"
}

if ($Publish) {
    Step "إنشاء إصدار GitHub v$version"
    $notes = Join-Path $root "dist\release-notes-$version.md"
    if (-not (Test-Path $notes)) { throw "ملاحظات الإصدار مفقودة: $notes" }
    gh release create "v$version" --draft --title "دفتري $version — Daftari $version" --notes-file $notes
    gh release upload "v$version" (Join-Path $root "dist\Daftari-$version.zip") --clobber
    Write-Host "المسودة جاهزة. للتفعيل: gh release edit v$version --draft=false --latest" -ForegroundColor Yellow
}

Step 'تم بنجاح'
Write-Host 'اختصار سطح المكتب يشير إلى dist\publish وقد صار محدّثاً.' -ForegroundColor Green

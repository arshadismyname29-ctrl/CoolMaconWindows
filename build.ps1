param([switch]$Run, [switch]$Verify, [ValidatePattern('^CoolMacDock(\.next)?\.exe$')][string]$OutputName = 'CoolMacDock.exe')
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compilerPath = Join-Path $frameworkPath 'csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'The Windows .NET Framework C# compiler is required.' }
$outputPath = Join-Path $projectRoot 'bin'
$executablePath = Join-Path $outputPath $OutputName
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$iconPath = Join-Path $projectRoot 'assets\Dock.ico'
if (-not (Test-Path -LiteralPath $iconPath)) { & (Join-Path $projectRoot 'tools\make-icon.ps1') -OutputPath $iconPath }
$references = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Web.Extensions.dll', 'System.Xaml.dll')
$compilerArgs = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/warn:4', ('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')), ('/out:' + $executablePath))
$compilerArgs += '/win32icon:' + $iconPath
foreach ($reference in $references) { $compilerArgs += '/reference:' + (Join-Path $frameworkPath $reference) }
foreach ($reference in @('WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll')) { $compilerArgs += '/reference:' + (Join-Path $frameworkPath ('WPF\' + $reference)) }
$compilerArgs += (Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
& $compilerPath @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Host ('Built ' + $executablePath)
if ($Verify) {
    $verificationPath = Join-Path $projectRoot 'artifacts'
    New-Item -ItemType Directory -Path $verificationPath -Force | Out-Null
    $verificationProcess = Start-Process -FilePath $executablePath -ArgumentList @('--verify', ('"' + $verificationPath + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($verificationProcess.ExitCode -ne 0) { throw ('Verification failed. See ' + $verificationPath) }
    Get-Content -LiteralPath (Join-Path $verificationPath 'verification.txt')
}
if ($Run) { Start-Process -FilePath $executablePath }

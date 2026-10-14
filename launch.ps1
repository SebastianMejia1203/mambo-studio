$ErrorActionPreference = 'Stop'
$projectDirectory = $PSScriptRoot
dotnet build (Join-Path $projectDirectory 'ScreenStudio.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar Mambo Studio.' }
Start-Process -FilePath (Join-Path $projectDirectory 'bin\Release\net10.0-windows\MamboStudio.exe') -WorkingDirectory $projectDirectory

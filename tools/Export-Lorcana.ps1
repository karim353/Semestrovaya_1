$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path $projectRoot '.local/submission'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$archivePath = Join-Path $outputRoot ("lorcana-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8) + '.zip')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$files = @(
    foreach ($name in @('.dockerignore', '.env.example', '.gitignore', 'Dockerfile', 'Lorcana.sln',
            'README.md', 'compose.yaml', 'compose.tests.yaml')) {
        Get-Item -LiteralPath (Join-Path $projectRoot $name)
    }
    foreach ($directory in @('Lorcana', 'Framework', 'MiniTemplateEngine', 'MyORMLibrary',
            'MiniTemplateEngineUnitTest', 'MyORMLibraryUnitTest', 'Lorcana.Tests', 'sql', 'tools', 'docs')) {
        Get-ChildItem -LiteralPath (Join-Path $projectRoot $directory) -File -Recurse |
            Where-Object {
                $_.FullName -notmatch '[\\/](bin|obj|TestResults|\.git|\.local)[\\/]' -and
                $_.Name -notlike '.env*' -and $_.Extension -notin @('.user', '.suo')
            }
    }
)
$archive = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        $entry = $file.FullName.Substring($projectRoot.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName,
            $entry, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
$check = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $bad = @($check.Entries | Where-Object {
        $_.FullName -match '(^|/)(\.git|\.local|bin|obj|TestResults)(/|$)' -or
        $_.Name -eq '.env' -or ($_.Name -like '.env.*' -and $_.Name -ne '.env.example')
    })
    if ($bad.Count) { throw 'В архиве найдены запрещённые служебные файлы. Не отправляйте его.' }
    foreach ($required in @('README.md', 'Dockerfile', 'compose.yaml', 'sql/01-schema.sql',
            'sql/02-catalog.sql', 'Lorcana.sln', 'docs/ASSETS.md',
            'Framework/MiniHttpServer.Framework.csproj',
            'MiniTemplateEngine/MiniTemplateEngine.csproj',
            'MyORMLibrary/MyORMLibrary.csproj', 'Lorcana/Lorcana.csproj', 'Lorcana/Pages/ListCards/index.html',
            'Lorcana/public/assets/gallery.css', 'MiniTemplateEngineUnitTest/MiniTemplateEngineUnitTest.csproj',
            'MyORMLibraryUnitTest/MyORMLibraryUnitTest.csproj',
            'Lorcana.Tests/Lorcana.Tests.csproj')) {
        if (-not $check.GetEntry($required)) { throw "В архиве отсутствует $required" }
    }
    Write-Host "Архив проверен: $($check.Entries.Count) файлов."
} finally { $check.Dispose() }
Write-Host "Для сдачи: $archivePath"

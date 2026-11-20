$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$runId = [Guid]::NewGuid().ToString('N')
$runRoot = Join-Path $projectRoot ".local/test-$runId"
$composeProject = "lorcana-tests-$runId"
$oldPassword = $env:LORCANA_TEST_PASSWORD
$docker = (Get-Command docker -ErrorAction SilentlyContinue).Source
if (-not $docker) { $docker = Join-Path $env:ProgramFiles 'Docker/Docker/resources/bin/docker.exe' }
if (-not (Test-Path -LiteralPath $docker)) { throw 'Docker Desktop не найден.' }

# В проверочную копию попадают только исходники, без .env, Git и тяжёлых изображений.
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
foreach ($directory in @('Lorcana', 'Framework', 'MiniTemplateEngine', 'MyORMLibrary',
        'MiniTemplateEngineUnitTest', 'MyORMLibraryUnitTest', 'Lorcana.Tests', 'sql')) {
    Get-ChildItem -LiteralPath (Join-Path $projectRoot $directory) -File -Recurse |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|cards)[\\/]' } |
        ForEach-Object {
            $relative = $_.FullName.Substring($projectRoot.Length + 1)
            $destination = Join-Path $runRoot $relative
            New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $destination
        }
}
foreach ($file in @('Lorcana.sln', 'compose.tests.yaml')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $runRoot
}
$env:LORCANA_TEST_PASSWORD = [Guid]::NewGuid().ToString('N')
$composeArgs = @('compose', '-f', (Join-Path $runRoot 'compose.tests.yaml'), '-p', $composeProject)
try {
    & $docker @composeArgs run --rm tests
    $testExit = $LASTEXITCODE
    if (Test-Path -LiteralPath (Join-Path $runRoot 'TestResults')) {
        $report = Join-Path $projectRoot ".local/test-results-$runId"
        Copy-Item -LiteralPath (Join-Path $runRoot 'TestResults') -Destination $report -Recurse
        Write-Host "Отчёт: $report"
    }
    if ($testExit -ne 0) { throw "Тесты завершились с кодом $testExit." }
} finally {
    # Останавливается только случайно названный тестовый проект; рабочая БД не затрагивается.
    & $docker @composeArgs down --volumes
    $cleanupExit = $LASTEXITCODE
    $env:LORCANA_TEST_PASSWORD = $oldPassword
    $allowedRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '.local')) + [IO.Path]::DirectorySeparatorChar
    $resolvedRun = [IO.Path]::GetFullPath($runRoot)
    if ($cleanupExit -eq 0 -and $resolvedRun.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolvedRun -Recurse -Force
    }
    if ($cleanupExit -ne 0) { throw 'Не удалось очистить тестовые контейнеры. Повторите docker compose down для тестового проекта.' }
}

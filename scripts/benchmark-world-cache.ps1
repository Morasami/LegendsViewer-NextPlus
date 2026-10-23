param(
    [string]$GameDirectory = 'D:\Steam\steamapps\common\Dwarf Fortress',
    [string]$ProbeDll = "$PSScriptRoot\WorldCacheProbe\bin\Debug\net10.0\WorldCacheProbe.dll",
    [string]$Output = "$PSScriptRoot\..\artifacts\cache-benchmarks"
)
$ErrorActionPreference = 'Stop'
$Output = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force $Output | Out-Null
$benchmarkCache = Join-Path $env:APPDATA ('LegendsViewer-NextPlus\cache\benchmarks\' + [Guid]::NewGuid().ToString('N'))
$rows = [Collections.Generic.List[object]]::new()
function Measure-Load([string]$label, [string]$mode, [string]$source) {
    Write-Host "Measuring $label in a fresh process"
    $elapsed = [Diagnostics.Stopwatch]::StartNew()
    $json = & dotnet $ProbeDll $mode $source $benchmarkCache
    if ($LASTEXITCODE -ne 0) { throw "Probe failed: $label" }
    $wall = $elapsed.Elapsed.TotalMilliseconds
    $row = ($json | Select-Object -Last 1) | ConvertFrom-Json
    $row | Add-Member -NotePropertyName label -NotePropertyValue $label
    $row | Add-Member -NotePropertyName processWallMs -NotePropertyValue $wall
    $rows.Add($row)
    $rows | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $Output 'results.json')
    Write-Host "$label : $([Math]::Round($row.fullMs)) ms, parser calls $($row.parserCalls), cache $($row.cacheBytes) bytes"
}
foreach ($region in @('region2-00184-01-01', 'region3-00103-07-20')) {
    $source = Join-Path $GameDirectory ($region + '-legends.xml')
    Measure-Load "$region baseline" baseline $source
    Measure-Load "$region cold" cold $source
    Measure-Load "$region warm 1" warm $source
    Measure-Load "$region warm 2" warm $source
    $copyDirectory = Join-Path $Output $region
    New-Item -ItemType Directory -Force $copyDirectory | Out-Null
    foreach ($suffix in @('-legends.xml','-legends_plus.xml','-world_history.txt','-world_sites_and_pops.txt','-world_map.csv','-world_map.bmp')) {
        $original = Join-Path $GameDirectory ($region + $suffix)
        if (Test-Path -LiteralPath $original) { Copy-Item -LiteralPath $original -Destination $copyDirectory }
    }
    $disposable = Join-Path $copyDirectory ($region + '-legends.xml')
    Measure-Load "$region disposable seed" cold $disposable
    [IO.File]::AppendAllText($disposable, "`n")
    Measure-Load "$region changed source" cold $disposable
}
Write-Host "Results: $Output\results.json"

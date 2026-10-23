param(
    [string]$GameDirectory = 'D:\Steam\steamapps\common\Dwarf Fortress',
    [string]$ProbeDll = "$PSScriptRoot\SearchProbe\bin\Debug\net10.0\SearchProbe.dll",
    [string]$CacheDirectory = "$env:APPDATA\LegendsViewer-NextPlus\cache",
    [string]$Output = "$PSScriptRoot\..\artifacts\search-benchmarks"
)
$ErrorActionPreference = 'Stop'
$Output = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force $Output | Out-Null
if (-not (Test-Path -LiteralPath $ProbeDll)) {
    throw "SearchProbe is not built. Run: dotnet build scripts\SearchProbe\SearchProbe.csproj"
}
$rows = [Collections.Generic.List[object]]::new()
foreach ($region in @('region2-00184-01-01', 'region3-00103-07-20')) {
    $source = Join-Path $GameDirectory ($region + '-legends.xml')
    if (-not (Test-Path -LiteralPath $source)) {
        Write-Warning "Missing export: $source"
        continue
    }
    Write-Host "Measuring $region in a fresh process"
    $elapsed = [Diagnostics.Stopwatch]::StartNew()
    $json = & dotnet $ProbeDll $source $CacheDirectory 'Urist' 'artifact created' 'war' 'a' 'zzzznotfound'
    if ($LASTEXITCODE -ne 0) { throw "SearchProbe failed for $region" }
    $row = $json | ConvertFrom-Json
    $row | Add-Member -NotePropertyName region -NotePropertyValue $region
    $row | Add-Member -NotePropertyName processWallMs -NotePropertyValue ([Math]::Round($elapsed.Elapsed.TotalMilliseconds, 1))
    $rows.Add($row)
    $rows | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $Output 'results.json')
    Write-Host ("{0}: build {1:N0} ms, {2:N0} records, ~{3:N1} MiB estimated, heap delta {4:N1} MiB" -f `
        $region, $row.indexBuildMs, $row.documents, ($row.estimatedIndexBytes / 1MB), ($row.managedHeapDeltaBytes / 1MB))
}
Write-Host "Results: $Output\results.json"

# Score green/yellow accuracy for ConnectionsSolver across all 5 test puzzles.
# Usage:  pwsh -File grade-puzzles.ps1 [-NoLabelRerank]
#         pwsh -File grade-puzzles.ps1 -ExtraArgs '--label-rerank-beta','2.0'
param(
    [switch]$NoLabelRerank,
    [string[]]$ExtraArgs = @()
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$proj     = Join-Path $repoRoot 'ConnectionsSolver\ConnectionsSolver.csproj'

$baseArgs = @('run','--project',$proj,'--no-build','--')
if ($NoLabelRerank) { $baseArgs += '--no-label-rerank' }
$baseArgs += $ExtraArgs

function Parse-Groups([string]$path) {
    $groups = @{}
    foreach ($line in Get-Content $path) {
        if ($line -match '^\s*#\s*(purple|green|yellow|blue)\s*-\s*[^:]+:\s*(.+)$') {
            $color = $matches[1]
            $words = ($matches[2] -split ',') | ForEach-Object { $_.Trim().ToLowerInvariant() }
            $groups[$color] = $words
        }
    }
    return $groups
}

function Run-Solver([string]$path) {
    # Feed "quit" so InteractiveSession exits at the first prompt after printing one round of analysis.
    $stdout = 'quit' | & dotnet @baseArgs $path 2>$null
    return $stdout -join "`n"
}

function Extract-AnchorGroups([string]$out) {
    # PrintAnchored emits: "  [A1   ]  <stars>  <score>  [labels]  leftover: X"
    #                       "           word1, word2, word3, word4"
    # (variants indented further). We pick the line right after a label-bearing line.
    $lines = $out -split "`n"
    $anchors = @()
    for ($i = 0; $i -lt $lines.Length - 1; $i++) {
        if ($lines[$i] -match '\[\s*(A\d+(?:v\d+)?)\s*\]') {
            $label = $matches[1]
            $next = $lines[$i + 1]
            # The words line is just comma-separated words (with optional <> for variants).
            $clean = ($next -replace '[<>]','').Trim()
            if ($clean -match ',') {
                $words = ($clean -split ',') | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ -ne '' }
                if ($words.Count -ge 4) {
                    $anchors += ,@{ Label = $label; Words = $words }
                }
            }
        }
    }
    return $anchors
}

function Score-Best($targetWords, $anchors) {
    $best = @{ Overlap = 0; Label = '-'; Words = @() }
    foreach ($a in $anchors) {
        $overlap = (Compare-Object $targetWords $a.Words -IncludeEqual -ExcludeDifferent -PassThru | Measure-Object).Count
        if ($overlap -gt $best.Overlap) {
            $best = @{ Overlap = $overlap; Label = $a.Label; Words = $a.Words }
        }
    }
    return $best
}

$rows = New-Object System.Collections.Generic.List[object]
$exact = 0; $three = 0; $two = 0
for ($n = 1; $n -le 5; $n++) {
    $puzzle = Join-Path $repoRoot "ConnectionsSolver\test-puzzle-$n.txt"
    $groups = Parse-Groups $puzzle
    $out = Run-Solver $puzzle
    $anchors = Extract-AnchorGroups $out
    foreach ($color in 'green','yellow') {
        if (-not $groups.ContainsKey($color)) { continue }
        $best = Score-Best $groups[$color] $anchors
        if ($best.Overlap -ge 4) { $exact++ }
        elseif ($best.Overlap -eq 3) { $three++ }
        elseif ($best.Overlap -eq 2) { $two++ }
        $rows.Add([pscustomobject]@{
            Puzzle = $n
            Color  = $color
            Target = ($groups[$color] -join ', ')
            Best   = "$($best.Label): $($best.Words -join ', ')"
            Hit    = "$($best.Overlap)/4"
        })
    }
}

$rows | Format-Table -AutoSize | Out-String -Width 200
Write-Host ""
Write-Host ("Summary: exact 4/4 = {0}, near 3/4 = {1}, weak 2/4 = {2}" -f $exact, $three, $two)

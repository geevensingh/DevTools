function IsClassItem($item) {
    return @("Hunter Cloak", "Titan Mark", "Warlock Bond") -contains $item.Type
}

$allWeights = @{
    "Hunter"  = @(
        @{
            Name       = "Basic"
            Mobility   = 10
            Resilience = 4
            Recovery   = 5
            Discipline = 4
            Intellect  = 7
            Strength   = 4
            Threshold  = 340
        }
    )
    "Titan"   = @(
        @{
            Name       = "Basic"
            Mobility   = 1
            Resilience = 10
            Recovery   = 5
            Discipline = 4
            Intellect  = 7
            Strength   = 4
            Threshold  = 310
        }
    )
    "Warlock" = @(
        @{
            Name       = "Basic"
            Mobility   = 1
            Resilience = 2
            Recovery   = 2
            Discipline = 2
            Intellect  = 3
            Strength   = 2
            Threshold  = 136    # effectively 68+ total
        },
        @{
            Name       = "Grenade"
            Mobility   = 0.1
            Resilience = 2
            Recovery   = 6
            Discipline = 7
            Intellect  = 5
            Strength   = 4
            Threshold  = 268
        }
        @{
            Name       = "Super"
            Mobility   = 0.1
            Resilience = 2
            Recovery   = 6
            Discipline = 5
            Intellect  = 7
            Strength   = 4
            Threshold  = 268
        }
    )
}

function GetWeights($item) {
    $class = $item.Equippable
    return $allWeights[$class]
}

function IsDupe($item, $collection) {
    return (GetDupes $item $collection).Count -gt 1
}

function GetDupes($item, $collection) {
    $collection | Where-Object { $_.Hash -eq $item.Hash -and $_.Tag -ne "junk" }
}

function GetWeightTotal($item, $weight) {
    $weightedTotal = 0
    @("Mobility", "Resilience", "Recovery", "Discipline", "Intellect", "Strength") | ForEach-Object {
        $stat = $_
        $weightedValue = ([int]$item.$stat) * $weight.$stat
        $weightedTotal += $weightedValue
    }

    $weightedTotal
}

$everything = Import-Csv -Path ~\Downloads\destinyArmor.csv

$allArmor = $everything |
    Where-Object { -not (IsClassItem $_) } |
    Where-Object { $_.Tier -ne "Rare"}

$allArmor | ForEach-Object {
    $item = $_
    @("Mobility", "Resilience", "Recovery", "Discipline", "Intellect", "Strength", "Total", "Custom") | ForEach-Object {
        $basePropertyName = $_ + " (Base)"
        $item.$_ = $item.$basePropertyName
        $item.PSObject.Properties.Remove($basePropertyName)
    }

    for ($ii = 0; $ii -le 20; $ii++) {
        $item.PSObject.Properties.Remove("Perks $ii")
    }

    $overallThreshold = $false
    $absoluteValue = 0
    (GetWeights $item) | ForEach-Object {
        $weight = $_
        $weightedTotal = GetWeightTotal $item $weight
        $item | Add-Member -MemberType NoteProperty -Name "$($weight.Name) Total" -Value $weightedTotal

        $absoluteValue += ($weightedTotal - $weight.Threshold) / $weight.Threshold * 100
        $meetsThreshold = $weightedTotal -ge $weight.Threshold
        $item | Add-Member -MemberType NoteProperty -Name "$($weight.Name) Threshold" -Value $meetsThreshold
        $overallThreshold = $overallThreshold -or $meetsThreshold
    }
    $item | Add-Member -MemberType NoteProperty -Name "Threshold" -Value $overallThreshold
    $item | Add-Member -MemberType NoteProperty -Name "AbsoluteValue" -Value $absoluteValue
}

$best = @{}
$allArmor |% {
    $armorType = $_.Type
    $modType = $_."Seasonal Mod"
    if (-not $best.$armorType) {
        $best[$armorType] = @{}
    }

    if (-not $best.$armorType.$modType) {
        $best[$armorType][$modType] = @($_)
    } else {
        $best[$armorType][$modType] += $_
    }
}

$best |% {Write-Host $_}

$allArmor = $allArmor | Where-Object { $_.Equippable -eq "Warlock" }
# $fileName = ".\" + [Guid]::NewGuid().ToString() + ".csv"
# $allArmor | Export-Csv -Path $fileName -NoTypeInformation
# & $fileName

$junk = @()

return 

$junk = $allArmor
$junk = $junk | Where-Object {
    if ($_.Tier -eq "Exotic") {
        # If this item isn't a dup, then we keep it
        if (-not (IsDupe $_ $allArmor)) { return $false }

        # $item = $_
        # $dupes = GetDupes $_ $allArmor
        # $dupes = $dupes | Where-Object { $_.Id -ne $item.Id }

        # $isOneStrictlyBetter = $false
        # $allDupesWorse = $true
        # $dupes | ForEach-Object {
        #     $dupe = $_
        #     $isDupeStrictlyWorse = $true
        #     $isDupeStrictlyBetter = $true
        #     (GetWeights $item) | ForEach-Object {
        #         $weight = $_
        #         $isDupeStrictlyWorse = $isDupeStrictlyWorse -and ($dupe."$($weight.Name) Total" -lt $item."$($weight.Name) Total")
        #         $isDupeStrictlyBetter = $isDupeStrictlyBetter -and ($dupe."$($weight.Name) Total" -gt $item."$($weight.Name) Total")
        #     }
        #     $allDupesWorse = $allDupesWorse -and $isDupeStrictlyWorse
        #     $isOneStrictlyBetter = $isOneStrictlyBetter -or $isDupeStrictlyBetter
        # }

        # if ($allDupesWorse) { return $false }
        # if ($isOneStrictlyBetter) { return $true }
    }
    return -not $_.Threshold
}
$junk = $junk | Where-Object {
    $item = $_
    $highSingleStat = 0
    $highDoubleStat = 0
    $highTripleStat = 0
    @("Mobility", "Resilience", "Recovery", "Discipline", "Intellect", "Strength") | ForEach-Object {
        $statValue = [int]$item.$_
        if ($statValue -ge 23) {
            $highSingleStat++
        }
        if ($statValue -ge 19) {
            $highDoubleStat++
        }
        if ($statValue -ge 17) {
            $highTripleStat++
        }
    }
    return $highSingleStat -lt 1 -and $highDoubleStat -lt 2 -and $highTripleStat -lt 3
}
$junk = $junk | Where-Object { ([int]$_."Masterwork Tier") -lt 10 }
# $junk = $junk | Where-Object { $_.Tag -ne "junk" }
$junk = $junk | Where-Object { $_.Tag -ne "favorite" }

if ($junk.Count -eq 0) {
    Write-Host "There is nothing to junk!"
    return
}

$filterString = ($junk | ForEach-Object { "id:" + $_.id }) -join " or "
Write-Host $filterString
$filterString | Set-Clipboard

param(
  [string]$Source = (Join-Path $PSScriptRoot '..\docs\RETAILER_ONBOARDING_OPERATIONS_GUIDE.md'),
  [string]$Output = (Join-Path $PSScriptRoot '..\docs\RETAILER_ONBOARDING_OPERATIONS_GUIDE.pdf')
)

$lines = [System.Collections.Generic.List[string]]::new()
foreach ($raw in [IO.File]::ReadAllLines((Resolve-Path $Source))) {
  $line = $raw.TrimEnd()
  if ([string]::IsNullOrWhiteSpace($line)) { $lines.Add(''); continue }
  $text = $line -replace '^#{1,6}\s+', '' -replace '\*\*', '' -replace '`', '' -replace '\|', ' | '
  $width = if ($line -match '^#\s') { 72 } elseif ($line -match '^##\s') { 82 } else { 105 }
  while ($text.Length -gt $width) {
    $cut = $text.LastIndexOf(' ', $width)
    if ($cut -lt 1) { $cut = $width }
    $lines.Add($text.Substring(0, $cut).Trim())
    $text = $text.Substring($cut).TrimStart()
  }
  $lines.Add($text)
}

$pageLines = 48
$pages = @()
for ($i = 0; $i -lt $lines.Count; $i += $pageLines) { $pages += ,@($lines[$i..([Math]::Min($i + $pageLines - 1, $lines.Count - 1))]) }
$objects = [System.Collections.Generic.List[string]]::new()
$objects.Add('<< /Type /Catalog /Pages 2 0 R >>')
$pageRefs = 3..($pages.Count + 2) | ForEach-Object { "$_ 0 R" }
$objects.Add("<< /Type /Pages /Kids [$($pageRefs -join ' ')] /Count $($pages.Count) >>")
for ($page = 0; $page -lt $pages.Count; $page++) {
  $content = "BT`n/F1 10 Tf`n45 770 Td`n"
  foreach ($line in $pages[$page]) {
    $escaped = $line.Replace('\','\\').Replace('(','\(').Replace(')','\)')
    $content += "/F1 9 Tf ($escaped) Tj`n0 -15 Td`n"
  }
  $content += 'ET'
  $contentBytes = [Text.Encoding]::ASCII.GetBytes($content)
  $contentObject = $objects.Count + 2
  $objects.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 $($contentObject + 1) 0 R >> >> /Contents $contentObject 0 R >>")
  $objects.Add("<< /Length $($contentBytes.Length) >>`nstream`n$content`nendstream")
  $objects.Add('<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>')
}
$outputBytes = [Collections.Generic.List[byte]]::new()
$outputBytes.AddRange([Text.Encoding]::ASCII.GetBytes("%PDF-1.4`n%`n"))
$offsets = [Collections.Generic.List[int]]::new()
for ($i = 0; $i -lt $objects.Count; $i++) { $offsets.Add($outputBytes.Count); $outputBytes.AddRange([Text.Encoding]::ASCII.GetBytes("$($i + 1) 0 obj`n$($objects[$i])`nendobj`n")) }
$xref = $outputBytes.Count
$outputBytes.AddRange([Text.Encoding]::ASCII.GetBytes("xref`n0 $($objects.Count + 1)`n0000000000 65535 f `n"))
foreach ($offset in $offsets) { $outputBytes.AddRange([Text.Encoding]::ASCII.GetBytes(('{0:0000000000} 00000 n ' -f $offset) + "`n")) }
$outputBytes.AddRange([Text.Encoding]::ASCII.GetBytes("trailer`n<< /Size $($objects.Count + 1) /Root 1 0 R >>`nstartxref`n$xref`n%%EOF`n"))
$outputDirectory = [IO.Path]::GetFullPath((Split-Path $Output -Parent))
[IO.File]::WriteAllBytes((Join-Path $outputDirectory (Split-Path $Output -Leaf)), $outputBytes.ToArray())

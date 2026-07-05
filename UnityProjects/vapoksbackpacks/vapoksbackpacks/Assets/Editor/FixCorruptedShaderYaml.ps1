# Fixes Valheim YAML shader assets where compressedBlob was wiped but bloated
# m_CompileInfo remained (Unity 6 YAML parser errors on numeric snippet keys).
# Run from repo root or pass -ShaderDir. Does NOT restore compressedBlob data.

param(
    [string]$ShaderDir = "$PSScriptRoot\..\Shader"
)

$compileInfoReplacement = @"
  m_CompileInfo:
    m_Snippets: {}
    m_MeshComponentsFromSnippets: 0
    m_HasSurfaceShaders: 0
    m_HasFixedFunctionShaders: 0
  m_CompileSmokeTestAfterImport:
"@

$fixed = 0
Get-ChildItem -Path $ShaderDir -Filter "Custom_*.asset" | ForEach-Object {
    $content = [IO.File]::ReadAllText($_.FullName)
    if ($content -notmatch '(?m)^  compressedBlob: \r?$') { return }

    $original = $content
    $content = $content -replace '(?m)^  platforms: \r?$', '  platforms: []'
    $content = $content -replace '(?m)^  compressedBlob: \r?$', '  compressedBlob: ""'
    $content = $content -replace '(?m)^  stageCounts: \r?$', '  stageCounts: []'
    $content = [regex]::Replace(
        $content,
        '(?s)  m_CompileInfo:.*?  m_CompileSmokeTestAfterImport:',
        $compileInfoReplacement)

    if ($content -ne $original) {
        [IO.File]::WriteAllText($_.FullName, $content)
        Write-Host "Fixed YAML parse structure: $($_.Name)"
        $fixed++
    }
}

Write-Host "Done. Fixed $fixed shader asset(s)."
Write-Host "NOTE: compressedBlob is still empty. Restore Assets/Shader from a clean copy for working Valheim shaders."

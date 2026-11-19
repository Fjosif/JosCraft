param([int]$TimeoutSeconds = 300)
$ErrorActionPreference = 'Stop'
# Current world verification includes editable voxels, survival and six-face
# textured geometry. Keep historical stage-two world-qa artifacts untouched.
& (Join-Path $PSScriptRoot 'verify_game.ps1') -TimeoutSeconds $TimeoutSeconds
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

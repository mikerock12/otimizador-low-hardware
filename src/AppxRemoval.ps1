param([Parameter(Mandatory=$true)][ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{1,159}$')][string]$PackageName)
# Protocolo local JSON; nao altera provisionamento, registro do Appx ou WindowsApps.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$messages = New-Object 'System.Collections.Generic.List[string]'
$removed = 0
$blocked = $false
function Installed($package) {
    return @($package.PackageUserInformation | Where-Object { [string]$_.InstallState -eq 'Installed' }).Count -gt 0
}
function Packages([string]$sid = '') {
    $parameters = @{ Name=$PackageName; PackageTypeFilter=@('Bundle','Main'); ErrorAction='Stop' }
    if ($sid) { $parameters.User = $sid } else { $parameters.AllUsers = $true }
    return @(Get-AppxPackage @parameters | Where-Object { $_.Name -eq $PackageName } | Sort-Object PackageFullName -Unique)
}
function Parents($packages) {
    # Remove-AppxPackage -AllUsers requer o pacote pai quando ha bundle.
    $bundles = @($packages | Where-Object { $_.IsBundle })
    $families = @($bundles | ForEach-Object { $_.PackageFamilyName })
    return @($packages | Where-Object { $_.IsBundle -or $families -notcontains $_.PackageFamilyName })
}
function Detail($errorRecord) {
    $text = $errorRecord.ToString()
    $codes = @([regex]::Matches($text, '0x[0-9a-fA-F]{8}') | ForEach-Object { $_.Value.ToUpperInvariant() } | Select-Object -Unique)
    if ($codes.Count) { return $codes -join ', ' }
    return 'erro de implantacao do Windows'
}
try {
    $initial = @(Packages)
    $installed = @($initial | Where-Object { Installed $_ })
    $families = @($installed | ForEach-Object { $_.PackageFamilyName })
    $targets = @(Parents @($initial | Where-Object { (Installed $_) -or ($_.IsBundle -and $families -contains $_.PackageFamilyName) }))
    if (-not $targets.Count) {
        $status = 'absent'
        if ($initial.Count) { $messages.Add('Sem instalacao ativa: existem apenas registros preparados/antigos. Nenhum arquivo do Windows foi apagado.') }
        else { $messages.Add('Este app ja esta ausente; nenhuma desinstalacao necessaria.') }
    } else {
        foreach ($package in $targets) {
            if ($package.NonRemovable -or $package.IsFramework -or $package.IsResourcePackage) {
                $blocked = $true
                $messages.Add('O Windows protege este pacote. Nao sera forcada a remocao de componentes do sistema.')
                continue
            }
            try {
                Remove-AppxPackage -Package $package.PackageFullName -AllUsers -ErrorAction Stop | Out-Null
                $removed++
            } catch {
                $messages.Add('Remocao global indisponivel (' + (Detail $_) + '); verificando os perfis que realmente possuem o app.')
                $sids = @($package.PackageUserInformation | Where-Object { [string]$_.InstallState -eq 'Installed' } | ForEach-Object { [string]$_.UserSecurityId } | Select-Object -Unique)
                foreach ($sid in $sids) {
                    # Nao tenta perfis removidos ou SIDs nao validos.
                    if ($sid -notmatch '^S-1-\d+(?:-\d+)+$') { continue }
                    try {
                        $perUser = @(Parents @(Packages $sid))
                        foreach ($item in $perUser) {
                            if ($item.NonRemovable -or $item.IsFramework -or $item.IsResourcePackage) { $blocked = $true; continue }
                            Remove-AppxPackage -Package $item.PackageFullName -User $sid -ErrorAction Stop | Out-Null
                            $removed++
                        }
                    } catch { $messages.Add('Um perfil nao concluiu a remocao (' + (Detail $_) + ').') }
                }
            }
        }
        # Nunca deduz sucesso apenas por codigo de saida ou por SilentlyContinue.
        $remaining = @(Packages | Where-Object { Installed $_ })
        if ($remaining.Count) {
            $status = if ($blocked) { 'blocked' } else { 'failed' }
            $messages.Add('O app ainda esta instalado em pelo menos um perfil. Feche o app e tente novamente. Se persistir 0x80070002, verifique DISM/SFC ou o diagnostico do Windows antes de repetir. A falha foi preservada no resumo.')
        } else {
            $status = if ($removed -gt 0) { 'removed' } else { 'absent' }
            $messages.Add('Verificacao final: nenhuma instalacao ativa deste app nos perfis do computador.')
        }
    }
} catch {
    $status = 'failed'
    $messages.Add('Nao foi possivel consultar ou remover o pacote (' + (Detail $_) + '). Verifique permissoes, servico AppX e integridade do Windows.')
}
@{ status=$status; removed=$removed; messages=@($messages.ToArray()) } | ConvertTo-Json -Depth 4 -Compress

$ErrorActionPreference = 'Stop'
$routine = Join-Path $PSScriptRoot '../src/AppxRemoval.ps1'
$count = 0
function Assert($condition, $message) { if (-not $condition) { throw $message }; $script:count++ }
function Package($bundle=$false, $states=@('Installed'), $protected=$false) {
    $users = @(); $i = 1000
    foreach ($state in $states) { $users += [pscustomobject]@{ UserSecurityId="S-1-5-21-100-200-300-$i"; InstallState=$state }; $i++ }
    [pscustomobject]@{ Name='Microsoft.Test'; PackageFullName=$(if ($bundle) {'Microsoft.Test_1_neutral_bundle'} else {'Microsoft.Test_1_x64_main'}); PackageFamilyName='Microsoft.Test_abc'; IsBundle=$bundle; IsFramework=$false; IsResourcePackage=$false; NonRemovable=$protected; PackageUserInformation=$users }
}
# Estas funcoes substituem os cmdlets somente no processo de teste. Nenhum pacote
# instalado, provisionamento, perfil ou registro real e alterado.
function Get-AppxPackage {
    param($Name, $PackageTypeFilter, [switch]$AllUsers, $User, $ErrorAction)
    $global:SltAppxTestState.queries++
    if ($global:SltAppxTestState.getError -or ($global:SltAppxTestState.postError -and $global:SltAppxTestState.queries -gt 1)) { throw 'Acesso negado 0x80070005' }
    if ($User) { return @($global:SltAppxTestState.packages | Where-Object { @($_.PackageUserInformation | Where-Object { $_.UserSecurityId -eq $User -and $_.InstallState -eq 'Installed' }).Count }) }
    return $global:SltAppxTestState.packages
}
function Remove-AppxPackage {
    param($Package, [switch]$AllUsers, $User, $ErrorAction)
    $global:SltAppxTestState.calls.Add(@{ package=$Package; all=[bool]$AllUsers; user=$User })
    if ($global:SltAppxTestState.race -and $AllUsers) { foreach ($p in $global:SltAppxTestState.packages) { $p.PackageUserInformation=@() }; throw '0x80070002' }
    if (($AllUsers -and $global:SltAppxTestState.globalError) -or ($User -and $global:SltAppxTestState.userError)) { throw 'Falha de implantacao 0x80070002' }
    if ($global:SltAppxTestState.noEffect) { return }
    foreach ($p in $global:SltAppxTestState.packages) {
        if ($p.PackageFullName -eq $Package -or $Package -like '*_bundle') {
            $p.PackageUserInformation = @($p.PackageUserInformation | Where-Object { -not $AllUsers -and $_.UserSecurityId -ne $User })
        }
    }
}
function Run($packages, $options=@{}) {
    $global:SltAppxTestState = @{ packages=@($packages); queries=0; calls=(New-Object 'System.Collections.Generic.List[object]') }
    foreach ($key in $options.Keys) { $global:SltAppxTestState[$key]=$options[$key] }
    (& $routine -PackageName 'Microsoft.Test') | ConvertFrom-Json
}
try {
    $r=Run @(); Assert ($r.status -eq 'absent') 'Ausente deve ser nao aplicavel'; Assert ($global:SltAppxTestState.calls.Count -eq 0) 'Nao remover app ausente'
    $r=Run @(Package $false @('Staged')); Assert ($r.status -eq 'absent') 'Staged nao e instalado'; Assert ($global:SltAppxTestState.calls.Count -eq 0) 'Nao remover somente staged'
    $r=Run @(Package); Assert ($r.status -eq 'removed') 'Remocao global normal'; Assert ($r.removed -eq 1) 'Contador global'
    $r=Run @((Package $true), (Package)); Assert ($r.status -eq 'removed') 'Bundle remove filhos'; Assert ($global:SltAppxTestState.calls.Count -eq 1) 'Somente pai deve ser removido'; Assert ($global:SltAppxTestState.calls[0].package -like '*_bundle') 'Remover bundle pai'
    $r=Run @((Package $true @('Staged')), (Package)); Assert ($r.status -eq 'removed') 'Bundle preparado com filho instalado'; Assert ($global:SltAppxTestState.calls[0].package -like '*_bundle') 'Preferir pai mesmo quando o registro instalado esta no filho'
    $r=Run @(Package $false @('Installed','Installed')) @{globalError=$true}; Assert ($r.status -eq 'removed') 'Fallback corrige 0x80070002 global'; Assert ($r.removed -eq 2) 'Dois perfis removidos'; Assert ($global:SltAppxTestState.calls.Count -eq 3) 'Global e dois perfis'; Assert (-not (($r.messages -join ' ') -match 'S-1-5-21')) 'Nao expor SID na mensagem'
    $r=Run @(Package) @{globalError=$true;userError=$true}; Assert ($r.status -eq 'failed') 'Falha real deve continuar visivel'; Assert (($r.messages -join ' ') -match '0X80070002') 'Preservar codigo de diagnostico'
    $r=Run @(Package $false @('Installed') $true); Assert ($r.status -eq 'blocked') 'Pacote protegido vira aviso'; Assert ($global:SltAppxTestState.calls.Count -eq 0) 'Nao forcar protegido'
    $r=Run @(Package) @{noEffect=$true}; Assert ($r.status -eq 'failed') 'Nao confiar em retorno sem erro: validar pos-condicao'
    $r=Run @(Package) @{getError=$true}; Assert ($r.status -eq 'failed') 'Consulta negada nao pode virar ausente'; Assert ($global:SltAppxTestState.calls.Count -eq 0) 'Nao remover apos consulta negada'
    $r=Run @(Package) @{postError=$true}; Assert ($r.status -eq 'failed') 'Sem verificacao final nao afirmar sucesso'
    $r=Run @(Package) @{race=$true}; Assert ($r.status -eq 'absent') 'Outra operacao ja removeu o app'; Assert ($r.removed -eq 0) 'Nao atribuir remocao externa ao programa'
    Write-Output "OK: $count verificacoes Appx com cmdlets simulados; nenhuma desinstalacao real."
} finally { Remove-Variable SltAppxTestState -Scope Global -ErrorAction SilentlyContinue }

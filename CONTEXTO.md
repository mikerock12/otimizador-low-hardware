# CONTEXTO.md — Otimizador Low Hardware

> Documento de contexto para continuidade do desenvolvimento em qualquer plataforma
> (outra IA, outro editor, outro dev). Lê-se este arquivo e entende-se **tudo** que o
> software é, como foi construído, por que cada decisão foi tomada e o que falta.
>
> Última atualização: 29/08/2026 (repositório tornado público, README reescrito com
> prints e exemplos de hardware, e §13.6 — terceiro incidente de falso-positivo,
> `MachineLearning/Anomalous` do Malwarebytes).

---

## 1. O que é o software

**Otimizador Low Hardware** — programa desktop Windows que detecta o hardware da
máquina e otimiza o **Windows 10 Pro e Windows 11 Pro** em máquinas fracas/antigas,
com três perfis (**Leve / Master / Ultra**), personalização item a item, reversão
completa e um passo final de **diagnóstico com nota de 1 a 10**.

- **Autor/dono**: Maicon Nunes — Smells Like Tech Informática — www.smellsliketech.com.br
- **Uso real**: ferramenta de bancada de assistência técnica; é levada em pen drive
  para a máquina do cliente (**um único .exe, sem instalação, sem dependências**).
- **Máquinas-alvo**: Acer Aspire E1-531 (Pentium B960, 4 GB, HDD), Samsung RV410
  (Pentium P6100, 2–4 GB, HDD), notebooks Samsung com Celeron 5205U + Windows 11, e
  similares — incluindo as que receberam upgrade de SSD e/ou RAM (a detecção muda as
  recomendações automaticamente).
- **Idioma da interface**: português do Brasil. Strings no código estão **sem
  acentos** (ex.: "Otimizacao") por segurança de encoding no compilador antigo — a
  exceção são os textos da splash/rodapé que usam acento e funcionam por serem UTF-8.

## 2. Stack e restrições de build (LEIA ANTES DE EDITAR CÓDIGO)

| Item | Valor |
|---|---|
| Linguagem | **C# limitado à sintaxe do C# 5** (motivo abaixo) |
| Framework | .NET Framework 4.x (4.8 nas máquinas reais; API usada é compatível 4.0+) |
| UI | WinForms, layout 100% em código (sem Designer, sem .resx) |
| Compilador | `csc.exe` embutido no Windows: `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` |
| Build | `build.bat` na raiz (funciona em qualquer Windows 10/11 **sem Visual Studio e sem SDK**) |
| Saída | `bin\OtimizadorWin10.exe` (~1,4 MB, autossuficiente) |
| Referências | `System.Management.dll` (WMI), `System.Web.Extensions.dll` (JSON via `JavaScriptSerializer`), `System.ServiceProcess.dll` (parar/iniciar serviços), `System.Drawing`/`System.Windows.Forms` (implícitas) |

**RESTRIÇÃO CRÍTICA — C# 5 apenas.** O `csc.exe` da pasta v4.0.30319 é o compilador
legado. **NÃO** usar: interpolação de string (`$"..."`), `?.` (null-conditional),
`nameof`, membros expression-bodied (`=>` em métodos/propriedades), inicializadores
de auto-propriedade, `out var`, tuplas, pattern matching, `using static`. **Pode**
usar: `var`, lambdas/`delegate`, LINQ, parâmetros opcionais, genéricos. Se o projeto
for migrado para Visual Studio/dotnet SDK moderno (csproj), essa restrição desaparece
— mas o alvo de runtime deve continuar .NET Framework 4.x (vem instalado no Win10/11;
.NET 6+ exigiria instalar runtime na máquina do cliente, o que é indesejado).

**Privilégios**: o exe exige administrador via `app.manifest`
(`requireAdministrator`) — necessário para serviços, HKLM, SMART e chkdsk. O
manifest também declara DPI-aware e suporte a Win8.1/10 (11 usa o GUID do 10).

**Recursos embutidos no build** (`build.bat`):
- `/win32icon:icon.ico` — ícone do exe (velocímetro azul, gerado programaticamente;
  gerador em scratchpad, não versionado — o `icon.ico` pronto está na raiz).
- `/win32manifest:app.manifest` — elevação UAC.
- `/resource:assets\logo.png,logo.png` — logo da empresa embutido; carregado em
  runtime via `Assembly.GetManifestResourceStream("logo.png")` com fallback para
  arquivo `logo.png` ao lado do exe.
- `src/AssemblyInfo.cs` — atributos de assembly que o csc converte no bloco
  **VERSIONINFO do Win32** (empresa, produto, versão, copyright). **Não remover**:
  binário sem metadados é sinal forte para antivírus heurístico (ver §13).
- Bloco final opcional de **assinatura Authenticode** via `signtool`, ativado pela
  variável de ambiente `OTIM_CERT_SUBJECT` (ver §13.3). Sem a variável, o build
  termina normal, apenas sem assinar.

## 3. Estrutura de arquivos

```
Software Otimizador/
├── build.bat            # compila tudo (csc /recurse:src\*.cs)
├── app.manifest         # requireAdministrator + dpiAware
├── icon.ico             # ícone multi-resolução (16..256px)
├── assets/logo.png      # logo Smells Like Tech (fundo escuro)
├── README.md            # documentação de usuário/racional (com prints e exemplos)
├── CONTEXTO.md          # este arquivo
├── .gitignore           # bin/, obj/, *.exe
├── docs/screenshots/    # prints usados no README (13 PNGs)
├── bin/OtimizadorWin10.exe
└── src/
    ├── Program.cs       # Main: Run(SplashForm) → Run(MainForm)
    ├── HardwareInfo.cs  # detecção WMI + recomendação de perfil
    ├── Catalog.cs       # catálogo de ~90 otimizações
    ├── Actions.cs       # ações atômicas (registro, serviço, cmd, tarefa, appx, limpeza)
    ├── Engine.cs        # aplicação, ponto de restauração, undo JSON, logs
    ├── Diagnostics.cs   # teste RAM, saúde SMART, chkdsk, benchmark, nota 1-10
    ├── MainForm.cs      # assistente de 5 passos + rodapé com créditos
    └── SplashForm.cs    # abertura 5s com fade in/out
```

Dados de runtime gravados em `%ProgramData%\OtimizadorWin10\`:
`undo_AAAAMMDD_HHMMSS.json` (reversão), `log_*.txt` (aplicação),
`diagnostico_*.txt` (relatórios). Undo revertido é renomeado para `*.revertido`.

## 4. Fluxo da interface (MainForm — 5 passos)

Janela fixa 920×650, wizard controlado por `MostrarTela(int n)` (telas construídas
em código a cada navegação; estado persiste em campos da classe).

0. **Hardware** — mostra `hw.Resumo()` (CPU, RAM, disco tipo/modelo/tamanho,
   Windows, bateria) em caixa de texto; detecção 100% automática (**não há mais
   seletor HDD/SSD** — foi removido a pedido do dono). Links: "somente diagnóstico"
   (pula para tela 4) e "reverter última otimização" (se existe undo).
1. **Perfil** — 3 cards RadioButton (Leve/Master/Ultra) com o recomendado destacado
   (`hw.TierRecomendado()`).
2. **Personalização** — ListView com checkboxes agrupada por categoria; painel
   direito mostra `DescricaoCompleta()` (descrição + lista literal de ações +
   ATENCAO). Itens com `Aviso` ficam laranja. Pré-marcação: `Tier <= tierEscolhido
   && !DesmarcadaPorPadrao`; remarcações do usuário sobrevivem à navegação
   (`idsMarcados`).
3. **Aplicar** — checkbox de ponto de restauração (marcado por padrão), barra de
   progresso, log escuro em tempo real. Roda em `Thread` com `BeginInvoke` para UI.
   Ao concluir, botão vira "Diagnostico >".
4. **Diagnóstico e nota** — ver §7. Botão "Reiniciar agora" aparece se houve
   otimização (shutdown /r /t 15).

**Splash** (antes do MainForm): 5000 ms totais, fade in/out de 700 ms via
`Opacity` + Timer 30 ms com relógio `Stopwatch` + `_offsetMs` (clique fora do link
salta o relógio para o início do fade out; clique no site abre navegador sem
fechar). **Rodapé permanente** do MainForm: logo 40 px clicável + "Criado por Maicon
Nunes — Smells Like Tech Informática" + link do site (`SplashForm.SITE_URL`).

## 5. Modelo de dados das otimizações

```csharp
class Optimization {
  string Id, Categoria, Nome, Descricao, Aviso;
  int Tier;                          // 1=Leve 2=Master 3=Ultra (tier MÍNIMO p/ pré-marcar)
  Func<HardwareInfo,bool> Aplicavel; // null = sempre; senão filtra por hardware/OS
  bool DesmarcadaPorPadrao;          // aparece mas exige opt-in (itens arriscados)
  List<OptAction> Acoes;
}
```

Condições prontas no `Catalog`: `SoHDD` (inclui disco desconhecido — assume HDD por
ser o comum nas máquinas-alvo), `SoSSD`, `PoucaRam` (≤4,5 GB), `SSDePoucaRam`,
`SoWin10` (= !Win11), `SoWin11`.

### Tipos de ação (Actions.cs) e reversão

Toda ação implementa `Describe()` (texto mostrado ao usuário), `CaptureUndo()`
(retorna `Dictionary<string,object>` com o estado ANTERIOR, ou null se
irreversível) e `Apply(log)`. Campo `BestEffort`: se true e a aplicação lançar
exceção, o Engine registra **AVISO** em vez de FALHA (usado quando outra ação do
mesmo item já garante o efeito — ver §9 "TaskbarDa").

| Classe | O que faz | Undo |
|---|---|---|
| `RegAction` | grava valor de registro (HKLM/HKCU, sempre `RegistryView.Registry64`) | valor anterior ou "não existia" (deleta) |
| `RegDeleteAction` | apaga valor de registro por API nativa (substituiu `reg.exe delete`) | recria o valor original (mesmo `Kind:"reg"`) |
| `ServiceControlAction` | para/inicia serviços via `System.ServiceProcess.ServiceController` (substituiu `cmd.exe /c net stop`) | sem undo (par parar/reiniciar no mesmo item) |
| `UninstallOneDriveAction` | mata OneDrive.exe e roda o `OneDriveSetup.exe /uninstall` oficial (substituiu cadeia `cmd.exe`+`taskkill`) | sem undo (reinstalável pelo site MS) |
| `ServiceAction` | muda `Start` do serviço direto no registro (2 auto/3 manual/4 desativado) + `sc stop`; ignora silenciosamente serviço inexistente | Start anterior |
| `CmdAction` | executa exe+args oculto com timeout | comando inverso opcional (ex.: `powercfg /hibernate on`) |
| `TaskAction` | `schtasks /Change /TN x /Disable` | `/Enable` |
| `AppxAction` | PowerShell `Get-AppxPackage -AllUsers \| Remove-AppxPackage` | sem undo (reinstalável na Store) |
| `CleanAction` | apaga arquivos de pastas (pula arquivos em uso), reporta MB liberados | sem undo |

**Formato do undo JSON** (lista em ordem inversa de aplicação; serializada com
`JavaScriptSerializer`): cada item tem `Kind` ("reg"/"service"/"cmd"/"task") +
campos próprios. `RegAction`: Hive, Path, Name, Existed, ValKind, Value (MultiString
com `\n`; Binary em base64). A reversão (`Engine.Reverter`) despacha pelo `Kind`.

### Engine.Aplicar (Engine.cs)

1. (Opcional) Ponto de restauração: zera `SystemRestorePointCreationFrequency`
   (remove limite de 24h), `Enable-ComputerRestore` + `Checkpoint-Computer` via
   PowerShell, timeout 3 min; falha não bloqueia (vira aviso).
2. Para cada ação: captura undo → aplica → loga OK/AVISO/FALHA; contadores em
   `ApplyResult {Sucesso, Falhas, Avisos, RestorePointOk, ArquivoUndo, ArquivoLog}`.
3. Grava undo JSON e log em `%ProgramData%\OtimizadorWin10\`.

## 6. Detecção de hardware (HardwareInfo.cs)

- **CPU**: `Win32_Processor` (nome, núcleos, threads, MHz).
- **RAM**: `Win32_ComputerSystem.TotalPhysicalMemory`.
- **Disco do sistema**: acha o índice físico do C: via ASSOCIATORS de
  `Win32_LogicalDisk→Win32_DiskPartition` (parse de "Disk #N"); guarda em
  `DiscoIndice` (usado pelo SMART). Modelo/tamanho via `Win32_DiskDrive`.
  **Tipo HDD×SSD** em cascata: (1) `MSFT_PhysicalDisk.MediaType` (3=HDD, 4=SSD) no
  namespace `ROOT\Microsoft\Windows\Storage`; (2) se não especificado,
  `SpindleSpeed` (0=SSD, >0=HDD); (3) heurística no nome do modelo ("SSD"/"NVME");
  (4) resto → assume HDD. `DiscoDetectadoComCerteza` indica se veio de (1)/(2).
- **Bateria**: `Win32_Battery` (usada só para o aviso do plano de energia).
- **Windows**: registro `CurrentVersion`. **PEGADINHA IMPORTANTE**: o Windows 11
  ainda escreve `ProductName = "Windows 10 Pro"` — a distinção é
  `CurrentBuildNumber >= 22000` → `EhWindows11` (e o nome exibido é corrigido).
  `SistemaSuportado = EhWindows10 || EhWindows11`; fora disso o programa avisa mas
  deixa continuar.
- **Recomendação de perfil** (`TierRecomendado`): score = HDD/desconhecido +2;
  RAM ≤2,5 GB +2 / ≤4,5 GB +1; CPU ≤2 núcleos e <2,6 GHz +1. Score ≥4 → Ultra,
  ≥2 → Master, senão Leve. (Validado: E1-531/RV410 originais → Ultra; E1-531 com
  SSD+8GB → Leve; Celeron 5205U 4GB+SSD → Master, +HDD → Ultra.)

## 7. Diagnóstico e nota (Diagnostics.cs)

`Diagnostics.Executar(hw, log, pct)` roda fora da UI thread. Etapas:

1. **Teste rápido de RAM**: aloca até 512 MB em blocos de 32 MB (reduz se faltar
   memória), 2 passadas de padrão determinístico + verificação; conta erros (para em
   100). Erros > 0 ⇒ nota final ≤ 2 e veredito manda testar/trocar módulo.
2. **Saúde do disco**: `MSFT_PhysicalDisk.HealthStatus`;
   `MSStorageDriver_FailurePredictStatus.PredictFailure` (root\wmi) — se true,
   alerta máximo de backup; espaço livre no C: (alerta <10%). **Detalhes SMART em
   duas fontes** (`LerSmartDetalhado`): (a) `MSFT_StorageReliabilityCounter`
   (PowerOnHours, Temperature, Wear → saúde SSD = 100−Wear); (b) fallback bruto
   `MSStorageDriver_FailurePredictData.VendorSpecific` — parse dos atributos SMART
   (entradas de 12 bytes a partir do offset 2: id, +3 valor normalizado, +5..+10
   raw little-endian): id 9 horas ligado, 194 temperatura, 5 setores realocados,
   231/233/177 vida útil SSD (valor normalizado ≈ %). Nem todo disco expõe desgaste
   (ex.: Lexar NQ100 não expõe) — nesse caso o programa **diz que não há o dado**
   em vez de inventar.
3. **Benchmark CPU**: loop misto int64/double em todas as threads por 800 ms
   (`Interlocked` + sink estático contra dead-code elimination) → MOPS.
4. **Benchmark RAM**: `Buffer.BlockCopy` de 64 MB ×12 → MB/s.
5. **Benchmark disco** (arquivo temp de 128 MB no %TEMP%): escrita sequencial com
   `FileOptions.WriteThrough`; leitura sequencial e **aleatória 4K (192 leituras)**
   com `FILE_FLAG_NO_BUFFERING` via P/Invoke (CreateFileW/ReadFile/VirtualAlloc —
   buffer alinhado obrigatório, por isso VirtualAlloc e não byte[]) para o cache do
   Windows não mascarar o resultado. 4K aleatório é o que separa HDD (~80 IOPS) de
   SSD (milhares).
6. **Só HDD** (`Disco != SSD`): **chkdsk C: somente leitura** (sem /f — não altera
   nada), saída transmitida ao vivo (`OutputDataReceived`, encoding OEM da cultura,
   filtra linhas de progresso "por cento"/"%" e sobrescritas com `\r`), timeout 15
   min. Análise do resumo: extrai "N KB em setores defeituosos" (pt/en) ⇒ alerta de
   dano físico; "encontrou problemas" (sem "não encontrou") ⇒ recomenda `chkdsk /f`,
   com ressalva de falso-positivo em volume em uso.

**Nota 1–10** (`Pontuar`, escala log2 — cada dobra de desempenho = pontos fixos):
- CPU: `5 + 1.5*log2(MOPS/1800)`
- RAM: `5 + 1.5*log2(MBs/6000)`
- Disco: `0.55*(3 + 1.7*log2(seqMédia/90)) + 0.45*(2 + 1.2*log2(IOPS4k/100))`
- Final: `0.35*CPU + 0.25*RAM + 0.40*Disco`, clamp [1,10]; erros de RAM ⇒ ≤2.
- **Calibração validada**: i5-6500 + SSD SATA ≈ 6,9 "Boa"; projeção Celeron
  5205U+HDD ≈ 3,5 "Limitada", +SSD ≈ 5,5. Vereditos: <3 Crítica, <5 Limitada,
  <6,5 Razoável, <8 Boa, ≥8 Excelente (texto sempre sugere SSD quando cabe).

Relatório completo é salvo em `%ProgramData%\OtimizadorWin10\diagnostico_*.txt`.

## 8. Catálogo de otimizações (Catalog.cs) — visão geral

~90 itens em 7 categorias. Princípios inegociáveis (comunicados na UI):
**nunca** desativa Windows Update, **nunca** desativa Windows Defender, nada de
tweaks instáveis. Tudo reversível (exceto remoção de apps, que volta pela Store).

- **Serviços** (cada um é um item individual, desmarcável): SysMain (Leve em HDD —
  é o maior ganho; só Ultra em SSD com pouca RAM), WSearch (Master em HDD, Ultra em
  SSD, com aviso), DiagTrack, dmwappushservice, DoSvc→manual + DODownloadMode=0,
  Fax, RemoteRegistry, MapsBroker, WerSvc, PhoneSvc, RetailDemo, WMPNetworkSvc,
  lfsvc, TabletInputService, WbioSrvc, wisvc, 4 serviços Xbox (item único),
  TrkWks; **opt-in** (DesmarcadaPorPadrao): Spooler (aviso impressora), DPS.
- **Privacidade/telemetria**: AllowTelemetry=0 (política), 7 tarefas agendadas de
  CEIP/Appraiser desativadas, ContentDeliveryManager (9 valores — mata apps
  promovidos/sugestões), apps em segundo plano (GlobalUserDisabled=1), Cortana
  (política + BingSearch), ID de publicidade.
- **Interface**: efeitos visuais p/ desempenho **mantendo ClearType**
  (VisualFXSetting=2 + valores individuais), transparência off, MenuShowDelay 100,
  News&Interests off (**só Win10**), GameDVR off (3 chaves).
- **Windows 11** (condição `SoWin11`): Widgets off (política Dsh +
  TaskbarDa BestEffort), Chat off (política ChatIcon=3 + TaskbarMn BestEffort),
  Copilot off (política + botão BestEffort), menu de contexto clássico (chave CLSID
  `{86ca1aa0-...}` em HKCU), destaques da pesquisa off, **VBS/HVCI off** (Ultra,
  **desmarcado por padrão**, aviso claro de que é recurso de segurança — decisão
  consciente: opt-in explícito).
- **Sistema/memória**: StartupDelayInMSec=0, SvcHostSplitThresholdInKB (agrupa
  svchost, só ≤4,5 GB RAM), OneDrive fora da inicialização, pagefile fixo (só HDD,
  tamanho calculado pela RAM), hibernação off (Ultra; desmarcada em HDD para não
  perder Fast Startup), plano Alto Desempenho (aviso se bateria).
- **Disco**: TRIM garantido (SSD), Prefetch off (**só SSD** — em HDD ajuda e não é
  oferecido).
- **Apps nativos** (um item por app, todos desmarcáveis): ~20 do Win10 (Bing*,
  GetHelp, Solitaire, Zune*, OfficeHub, YourPhone, Maps...) + 9 do Win11 (Teams
  pessoal, MSTeams, Clipchamp, To Do, Power Automate, Dev Home, GamingApp, novo
  Outlook, WebExperience/Widgets). Apps com uso legítimo têm aviso e/ou vêm
  desmarcados (Email/Calendário, Câmera, Captura, novo Outlook). Paint 3D só Win10.
- **Limpeza**: temporários (%TEMP% + Windows\Temp), cache do Windows Update
  (para/religa wuauserv+bits), miniaturas do Explorer.

## 9. Problemas conhecidos e soluções já aplicadas (não regredir!)

1. **Win11 bloqueia escrita de `TaskbarDa`/`TaskbarMn`/`ShowCopilotButton`**
   (UnauthorizedAccessException até como admin, em builds novas). Solução: essas
   escritas são `BestEffort=true` (viram AVISO no log) e **as políticas
   equivalentes** (Dsh/AllowNewsAndInterests, Windows Chat/ChatIcon,
   TurnOffWindowsCopilot) garantem o efeito. Confirmado em máquina real de cliente.
2. **Win11 se identifica como "Windows 10" no registro** → usar build ≥ 22000.
3. **Registro 64-bit**: sempre `RegistryKey.OpenBaseKey(..., RegistryView.Registry64)`
   para não cair no redirecionamento WOW64.
4. **chkdsk com saída em codepage OEM** e progresso com `\r` → encoding
   `TextInfo.OEMCodePage` + filtro de linhas de progresso.
5. **FILE_FLAG_NO_BUFFERING exige buffer alinhado** → VirtualAlloc, não byte[].
6. **JIT eliminando o loop de benchmark** → sink estático + Interlocked.
7. **Ícone não atualiza no Explorer** após recompilar → cache de ícones do Windows
   (copiar o exe para outra pasta resolve; máquina nova não sofre disso).
8. **Antivírus/SmartScreen tratando o exe como malware** — em 23/07/2026 o Google
   Drive bloqueou o arquivo como suspeito e o **Windows Defender apagou o exe** na
   máquina de um cliente. Causas e correções aplicadas estão detalhadas na **§13**
   (leitura obrigatória antes de mexer em qualquer chamada de processo externo).
9. **HKCU sob elevação**: o programa roda elevado na mesma conta do usuário, então
   HKCU é a conta certa (só seria problema se o cliente logasse como usuário padrão
   e elevasse com conta de admin diferente — cenário raro na bancada; limitação
   conhecida e aceita).

## 10. Estado de testes

- **Testado em máquina real** (dev: i5-6500, 16 GB, SSD Lexar, Win10 Pro 22H2):
  compilação, detecção, catálogo, benchmark completo, SMART (horas/temp/realocados
  OK; Wear não exposto pelo Lexar — mensagem honesta), calibração da nota (6,8–6,9).
- **Testado em máquina real de cliente** (notebook Win11): otimização aplicada com
  sucesso; único erro era o TaskbarDa, já corrigido (§9.1).
- **NÃO testado ainda**: caminho do chkdsk (só ativa em HDD — testar na primeira
  máquina de cliente com HDD); reversão completa de uma otimização grande; splash
  com fade em máquina muito lenta (Opacity em WinForms é leve, deve ser ok);
  simulações por hardware falso validaram os perfis (harness de console no
  scratchpad, não versionado).

## 11. Backlog / ideias futuras (nada disso foi começado)

- **Comprar o certificado Authenticode e assinar** (§13.3) — maior pendência aberta.
- Testar/ajustar parsing do chkdsk em HDD real.
- Gerenciador de programas de inicialização de terceiros (hoje só remove o atraso
  artificial e o OneDrive).
- Exportar relatório de diagnóstico em PDF/HTML com o logo (hoje é .txt).
- Modo linha de comando silencioso (`/perfil ultra /aplicar`) para automação de bancada.
- Comparativo antes/depois (rodar benchmark antes da otimização e mostrar delta).
- Verificação de atualização do próprio programa (checar versão no site).
- Tradução do resumo SMART por fabricante (TBW estimado etc.).
- Instalador opcional / versão com auto-update para clientes finais.

## 12. Como buildar e distribuir (resumo operacional)

```bat
build.bat
```
→ `bin\OtimizadorWin10.exe`. Distribuição = copiar **só o exe** (logo e ícone vão
dentro). Na máquina do cliente: executar → UAC "Sim" → (se SmartScreen) "Executar
mesmo assim".

**Git/GitHub:** remoto em `https://github.com/mikerock12/otimizador-low-hardware`,
propriedade da conta `mikerock12` (Maicon Nunes), branch `main`. Desde **29/08/2026 o
repositório é público** — decisão do dono, com dois objetivos: transparência (qualquer
um lê o fonte e recompila, o que é argumento direto contra o falso-positivo de
antivírus) e elegibilidade ao **Certum Open Source Code Signing** (§13.3/§13.6).
O `.gitignore` mantém `bin/`, `obj/` e `*.exe` fora do versionamento: builds devem ser
publicados como **Release do GitHub**, não commitados. Os prints do README ficam em
`docs/screenshots/`.

---

## 13. Falso-positivo de antivírus (Defender/Google Drive) — diagnóstico e correções

**Incidente (23/07/2026):** o Google Drive bloqueou o `.exe` como "arquivo suspeito"
e o **Windows Defender apagou o executável** durante a instalação na máquina de um
cliente.

Isso é um **falso positivo**, mas não é arbitrário: o programa reunia quase todos os
sinais que os motores heurísticos e de machine learning usam para classificar um
binário desconhecido como malicioso. Abaixo, o que causava, o que já foi corrigido no
código e o que só se resolve fora dele.

### 13.1 Por que era detectado

| Sinal | Situação anterior | Peso |
|---|---|---|
| Binário sem metadados (empresa/produto/versão) | nenhum metadado | **muito alto** — quase todo malware compila sem identificação |
| Sem assinatura digital | não assinado | **muito alto** |
| Reputação zero | exe novo a cada build, sem histórico | alto (SmartScreen/Defender são reputacionais) |
| `powershell.exe -ExecutionPolicy Bypass` oculto | 2 chamadas | **muito alto** — marca registrada de script malicioso |
| `cmd.exe /c ... & ... 2>nul` + `taskkill /f` | desinstalação do OneDrive | alto — sintaxe idêntica a dropper |
| `reg.exe delete` por processo elevado | remoção do OneDrive do Run | médio-alto |
| Processo elevado editando HKLM/serviços em lote | inerente à função | médio (não removível — é o que o programa faz) |
| Desativar serviços e tarefas do Windows | inerente | médio (comportamento de "system tampering") |

Os três últimos são **inerentes a um otimizador** e não devem ser "escondidos" — a
resposta correta para eles é **assinatura + reputação**, não disfarce.

### 13.2 Correções já aplicadas no código (23/07/2026)

1. **`src/AssemblyInfo.cs` criado** → o exe agora carrega VERSIONINFO completo
   (CompanyName "Smells Like Tech Informatica", ProductName, FileDescription,
   FileVersion 1.0.0.0, Copyright). Verificável em Propriedades → Detalhes.
   **Nunca remover este arquivo.**
2. **`-ExecutionPolicy Bypass` eliminado.** Descoberta importante: a execution policy
   **só se aplica a arquivos `.ps1`**, jamais a comandos inline (`-Command`) — o
   parâmetro era inútil e só servia para casar com assinaturas de antivírus. A única
   chamada PowerShell restante (remoção de apps Appx, que não tem equivalente limpo
   no .NET Framework) agora usa apenas `-NoProfile -NonInteractive -Command`.
3. **Ponto de restauração sem PowerShell** — `Engine.CriarPontoRestauracao()` passou
   a usar a classe WMI `SystemRestore` (`root\default`, métodos `Enable` e
   `CreateRestorePoint`). Eliminou o padrão "processo elevado gera shell oculto".
4. **`reg.exe delete` → `RegDeleteAction`** (API nativa de registro, com undo que
   recria o valor original).
5. **`cmd.exe /c net stop wuauserv & net stop bits` → `ServiceControlAction`**
   (`System.ServiceProcess.ServiceController`, sem shell).
6. **Cadeia `cmd.exe`+`taskkill /f`+`if exist` do OneDrive → `UninstallOneDriveAction`**
   (mata o processo pela API .NET e chama o `OneDriveSetup.exe /uninstall` oficial).

**Resultado:** restou **uma única** invocação de PowerShell (Appx), sem flags
suspeitas, e **zero** uso de `cmd.exe`, `reg.exe`, `taskkill` e `net stop`.
Regressão verificada: catálogo continua com 92 itens e os perfis recomendados por
máquina não mudaram.

> **Regra para quem continuar o projeto:** ao adicionar otimizações, **prefira sempre
> API nativa .NET/WMI a chamar um executável de shell**. Se precisar mesmo de um
> processo externo, use o binário final direto (ex.: `powercfg.exe`, `schtasks.exe`)
> — nunca `cmd.exe /c` com encadeamento `&`, nunca `-ExecutionPolicy Bypass`.

### 13.3 O que falta (fora do código) — em ordem de eficácia

1. **Certificado de assinatura de código (Authenticode)** — a correção definitiva.
   **Não há substituto.** Nenhuma alteração de código elimina a detecção enquanto o
   binário for anônimo (ver §13.5).
   - Desde jun/2023 **todo** certificado publicamente confiável (OV ou EV) exige a
     chave privada em **token USB físico ou HSM na nuvem** — não existe mais o .pfx
     simples baixado.
   - **Azure Trusted Signing (Microsoft)** — ~US$ 10/mês (≈US$ 120/ano), **sem token
     físico**, assinatura via serviço na nuvem. Hoje é a melhor relação
     custo/benefício e, por ser operado pela Microsoft, ajuda na reputação junto ao
     Defender/SmartScreen. Exige verificação da pessoa jurídica (CNPJ) com histórico
     mínimo de ~3 anos. **Verificar se o Brasil está na lista de países atendidos** —
     a cobertura vem sendo expandida e esse é o único ponto a confirmar antes de
     contratar.
   - **EV (Extended Validation)**: ~US$ 250–600/ano. Dá **reputação SmartScreen
     imediata** — resolve na hora, é o mais indicado para quem chega na máquina do
     cliente e precisa que funcione no mesmo dia.
   - **OV/padrão**: ~US$ 150–400/ano (Sectigo, SSL.com, Certum). Mais barato, mas
     **não dá mais confiança instantânea** no SmartScreen: a reputação se constrói com
     downloads/tempo.
   - **Certum Open Source Code Signing**: ~US$ 100–150 — via barata legítima, porém
     **exige o projeto ser open source público**. ✅ **Requisito atendido desde
     29/08/2026**: o repositório `mikerock12/otimizador-low-hardware` passou a ser
     **público**, então esta via está liberada (ver §13.6).
   - ⚠️ **Certificado ICP-Brasil (e-CNPJ A1/A3) NÃO serve para Authenticode** — as
     raízes da ICP-Brasil não estão no Microsoft Trusted Root Program para assinatura
     de código. É o erro mais comum de quem está no Brasil.
   - O `build.bat` **já está preparado**: defina `OTIM_CERT_SUBJECT` com o nome do
     titular do certificado e o build assina sozinho com SHA-256 + carimbo de tempo
     (timestamp é essencial: sem ele a assinatura "morre" quando o certificado
     expira).

2. **Submeter o falso positivo à Microsoft** (grátis, 1–3 dias, funciona):
   https://www.microsoft.com/en-us/wdsi/filesubmission — escolher "Software developer"
   e marcar como detecção incorreta. **Precisa ser refeito a cada build novo**, pois
   a análise é por hash — outro motivo para assinar (o certificado dá reputação ao
   *editor*, não só ao arquivo).

3. **Conferir no VirusTotal** antes de distribuir cada versão
   (https://www.virustotal.com) — mostra quantos e quais motores sinalizam. Observar:
   o VirusTotal **compartilha publicamente** as amostras enviadas; enviar o próprio
   binário é aceitável, mas é uma decisão do dono do software.

4. **Distribuição fora do Google Drive.** O Drive sinaliza `.exe` por política, mesmo
   assinado. Melhor: página de download no **www.smellsliketech.com.br** (HTTPS,
   com hash SHA-256 publicado ao lado do link, o que também transmite profissionalismo
   e permite ao cliente conferir o arquivo). Alternativa paliativa: enviar em `.zip`.

### 13.4 O que NÃO fazer (importante)

- ❌ **Não orientar clientes a desativar o Defender** nem a criar exclusões amplas
  (ex.: excluir `C:\` ou a pasta de downloads inteira). Além de deixar a máquina do
  cliente vulnerável, transfere para ele um risco que é do fornecedor do software.
- ❌ **Não ofuscar, comprimir ou "packar" o executável** (UPX, protetores, crypters).
  O efeito é o **oposto** do desejado: packers *aumentam* a taxa de detecção, porque
  são usados justamente para esconder malware.
- ❌ **Não tentar mascarar o comportamento** (renomear processos, adiar ações para
  driblar sandbox, etc.). O programa faz alterações legítimas de sistema e deve
  declará-las abertamente — a estratégia correta é **provar procedência**
  (metadados + assinatura + reputação), não esconder o que faz.
- Se o Defender apagar o exe durante o desenvolvimento nesta máquina, restaurar por
  Segurança do Windows → Proteção contra vírus → Histórico de proteção, e **não** por
  desativação da proteção em tempo real.

### 13.5 Segundo incidente (25/07/2026) — limite do que o código resolve

Mesmo após todas as correções da §13.2, o Defender passou a classificar o arquivo
como **Trojan** em algumas máquinas e o **Google Chrome bloqueou o download**
("arquivo com vírus"). Uma última limpeza foi feita (`sc.exe stop` →
`ServiceController`), mas a conclusão técnica é definitiva:

> **A margem de correção por código está esgotada.** O que resta detectando não são
> defeitos do programa — é a ausência de identidade verificável, somada a
> comportamentos que são a **própria função** do software.

Por que continua sendo sinalizado, mesmo com o código limpo:

1. **Binário anônimo com reputação zero.** Defender e Google Safe Browsing são
   **reputacionais**. Um `.exe` novo, sem assinatura, baixado por pouquíssimas
   pessoas, é tratado como desconhecido — e "desconhecido + pede administrador" é
   classificado como risco por padrão. Cada build gera um hash novo, então a
   reputação nunca se acumula.
2. **O comportamento do produto coincide com técnicas de malware.** Desativar
   telemetria (DiagTrack), relatório de erros, tarefas agendadas e serviços do
   Windows é, na taxonomia MITRE ATT&CK, *Defense Evasion* — é literalmente o que um
   trojan faz para se esconder. **Isso não pode nem deve ser removido: é o produto.**
   Detecções com sufixo `!ml` (ex.: `Trojan:Win32/Wacatac.B!ml`) indicam justamente
   veredito de machine learning por comportamento, não assinatura de vírus real.
3. **Chrome (Safe Browsing)** pesa fortemente a assinatura do editor e o volume de
   downloads do domínio de origem.

**Plano correto, em ordem:**

1. **Assinar o executável** (§13.3) — resolve 1 e 3, e reduz drasticamente 2, porque
   o veredito deixa de ser sobre um binário anônimo e passa a ser sobre um editor
   identificado e responsabilizável.
2. **Submeter como falso positivo à Microsoft** a cada versão, até a reputação firmar:
   https://www.microsoft.com/en-us/wdsi/filesubmission (grátis, 1–3 dias, opção
   "Software developer" → detecção incorreta).
3. **Chrome/Safe Browsing**: hospedar em `https://www.smellsliketech.com.br` (não no
   Google Drive), verificar o domínio no **Google Search Console** (mostra em
   "Problemas de segurança" se o site foi marcado) e, se houver bloqueio, contestar em
   https://safebrowsing.google.com/safebrowsing/report_error/
4. **Paliativo enquanto não há certificado**: distribuir em **.zip** (reduz o bloqueio
   no download, embora não elimine a detecção na execução) e publicar o **SHA-256** ao
   lado do link, para o cliente conferir a integridade.
5. **Publicar builds como Release do GitHub** em vez de anexar `.exe` ao repositório —
   com o certificado, o executável assinado vai na release.

**Expectativa realista a comunicar ao cliente/usuário:** sem certificado, o aviso vai
continuar aparecendo em parte das máquinas. Com certificado EV ou Azure Trusted
Signing, o problema deixa de existir na prática em poucos dias.

### 13.6 Terceiro incidente (29/08/2026) — Malwarebytes `MachineLearning/Anomalous`

O **Malwarebytes** passou a detectar o executável como **`MachineLearning/Anomalous`**
(o dono colocou o arquivo nas exceções da própria bancada para continuar trabalhando).

**Diagnóstico: é o mesmo fenômeno da §13.5, agora em outro fabricante.** O prefixo
`MachineLearning/` e o termo `Anomalous` são a nomenclatura do Malwarebytes para
veredito de **modelo estatístico**, equivalente ao sufixo `!ml` do Defender. Não há
família de malware identificada — não existe uma; o motor apenas classificou como
"anômalo" um binário sem assinatura, sem reputação, que pede administrador e desativa
serviços/tarefas do Windows.

**Verificação feita no código nesta data** (nada regrediu desde a §13.2):

- `grep` em `src/` confirma **zero** ocorrências de `cmd.exe`, `reg.exe`, `taskkill`,
  `net stop` e `-ExecutionPolicy Bypass`;
- resta **uma única** chamada `powershell.exe -NoProfile -NonInteractive -Command`
  (`Actions.cs:457`, remoção de Appx);
- `src/AssemblyInfo.cs` intacto — VERSIONINFO completo no binário;
- **nenhuma API de rede no projeto**: não há `System.Net`, `WebClient`,
  `HttpWebRequest`, `HttpClient` nem socket. Os três únicos `Process.Start` são
  `RunHidden` (powercfg/schtasks/fsutil/powershell), `shutdown.exe` (botão de reiniciar)
  e a URL do site aberta no navegador. Esse é um argumento forte a usar em contestação
  de falso positivo.

**Conclusão inalterada:** não há mais o que corrigir por código. Substituir o último
PowerShell exigiria consumir o WinRT `Windows.Management.Deployment.PackageManager`, o
que obrigaria a referenciar `Windows.winmd` do Windows SDK e **quebraria a restrição de
build sem SDK** (§2) — troca ruim, e sem garantia de mudar o veredito de um modelo que
julga comportamento, não a forma da chamada.

**Ações desta data:**

1. ✅ Repositório tornado **público** — além da transparência (qualquer um lê o código e
   recompila), isso **destrava o Certum Open Source Code Signing** (~US$ 100–150, §13.3),
   que era a via barata bloqueada pelo repositório privado.
2. ✅ README ganhou a seção **"Antivírus: por que aparece alerta e por que o software é
   seguro"**, escrita para o cliente final: explica o que significa `!ml`/
   `MachineLearning/Anomalous`, por que o programa cai nesse perfil, o que já foi
   limpo no código, como qualquer um verifica (ler o fonte, recompilar com `build.bat`,
   VirusTotal) e como agir se o antivírus bloquear — **sem** orientar a desativar
   proteção ou excluir pastas inteiras (§13.4).
3. ⏳ Pendente: reportar o falso positivo ao Malwarebytes
   (https://www.malwarebytes.com/support → false positive) e à Microsoft
   (https://www.microsoft.com/en-us/wdsi/filesubmission). Refazer a cada build.
4. ⏳ Pendente e prioritário: **contratar o certificado e assinar** (Certum OSS agora
   elegível, ou Azure Trusted Signing). É a única coisa que encerra o assunto.

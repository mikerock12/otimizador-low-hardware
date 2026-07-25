# Otimizador Low Hardware — Windows 10/11

> Criado por Maicon Nunes, da Smells Like Tech Informática — www.smellsliketech.com.br

Programa desktop (WinForms, .NET Framework 4.8) que identifica o hardware da máquina e
otimiza o Windows 10 **ou Windows 11** com três níveis — **Leve**, **Master** e
**Ultra** — mostrando ao usuário exatamente o que será feito e permitindo personalizar
item por item (ex.: não desinstalar determinado app nativo, não desativar determinado
serviço).

Público-alvo: notebooks antigos como **Acer Aspire E1-531** (Pentium B960, 4 GB, HDD) e
**Samsung RV410** (Pentium P6100, 2–4 GB, HDD), inclusive quando receberam upgrade de
RAM e/ou troca de HDD por SSD — a detecção na inicialização muda o conjunto de
otimizações recomendadas. Também cobre máquinas fracas de fábrica com Windows 11,
como notebooks Samsung com Celeron 5205U.

A versão do Windows é detectada pelo **build** (≥ 22000 = Windows 11 — o registro do
Windows 11 ainda se declara "Windows 10" no ProductName). Itens específicos de cada
versão só aparecem na versão certa.

## Como compilar

Não precisa de Visual Studio nem de SDK: usa o compilador `csc.exe` do .NET Framework
4.x, presente em qualquer Windows 10.

```bat
build.bat
```

Saída: `bin\OtimizadorWin10.exe` (pede elevação de administrador ao abrir — necessário
para alterar serviços e chaves HKLM).

## Fluxo do programa

1. **Detecção de hardware** (WMI): CPU, RAM, disco de sistema (tipo HDD × SSD via
   `MSFT_PhysicalDisk.MediaType` + `SpindleSpeed`, com fallback por modelo; marca,
   modelo e tamanho listados nas especificações), bateria e versão do Windows —
   totalmente automática, upgrades de SSD/RAM são reconhecidos sem perguntar nada.
2. **Perfil recomendado**: pontuação por HDD, pouca RAM e CPU fraca decide entre
   Leve, Master e Ultra (ex.: HDD + 4 GB + Pentium → Ultra; SSD + 8 GB → Leve).
3. **Personalização**: lista com ~70 otimizações agrupadas por categoria, cada uma com
   descrição do que será feito e avisos de efeitos colaterais; tudo pode ser
   marcado/desmarcado.
4. **Aplicação**: cria ponto de restauração, aplica com log em tempo real e grava um
   **arquivo de reversão** (`%ProgramData%\OtimizadorWin10\undo_*.json`) com o estado
   anterior de cada serviço/chave de registro.
5. **Diagnóstico e nota** (passo 5, também acessível direto da tela inicial): teste
   rápido de RAM (padrões + verificação, 512 MB), saúde do disco (SMART/predição de
   falha + status WMI + espaço livre, **horas ligado, temperatura e setores
   realocados** via `MSFT_StorageReliabilityCounter` com fallback em atributos SMART
   brutos; em **SSD**, saúde 0–100% pelo indicador de desgaste; em **HDD**, relatório
   `chkdsk` somente leitura com setores defeituosos e problemas de sistema de
   arquivos) e benchmark de CPU (multi-thread), banda de RAM e disco (escrita
   sequencial write-through, leitura sequencial e aleatória 4K sem cache via
   `FILE_FLAG_NO_BUFFERING`). Gera **nota de 1 a 10** (disco 40%, CPU 35%, RAM 25%,
   escala logarítmica — i5-6500+SSD ≈ 7; Celeron 5205U+HDD ≈ 3,5) com veredito e
   relatório salvo.
6. **Reversão**: link na tela inicial restaura a última otimização aplicada (apps
   desinstalados voltam pela Microsoft Store).

Falhas não críticas (ex.: builds novas do Windows 11 bloqueiam a escrita de
`TaskbarDa`/`TaskbarMn`/`ShowCopilotButton` mesmo para admin) são tratadas como
**aviso**, pois as políticas correspondentes já aplicam o efeito — o log distingue
FALHA de AVISO.

## Racional das otimizações (resumo da pesquisa)

- **HDD**: SysMain/Superfetch é a causa nº 1 de disco a 100% no Windows 10 antigo;
  indexação do Windows Search disputa I/O; arquivo de paginação fixo evita
  fragmentação; Prefetch é **mantido** (ajuda no HDD). Fast Startup é mantido por
  padrão (a desativação da hibernação vem desmarcada em HDD).
- **SSD (upgrade)**: SysMain/indexação deixam de ser críticos (só entram no Ultra);
  garante TRIM ativo; Prefetch pode ser desativado; hibernação pode ser removida para
  liberar espaço.
- **Pouca RAM (2–4 GB)**: apps em segundo plano desativados, agrupamento de processos
  svchost (`SvcHostSplitThresholdInKB`), remoção de apps nativos, Cortana e serviços
  ociosos (Xbox, biometria, telefonia, fax...).
- **CPU/GPU fracas**: efeitos visuais em "melhor desempenho" (mantendo ClearType),
  sem transparência, plano de energia Alto desempenho (com aviso de bateria).
- **Sempre**: telemetria no mínimo, tarefas agendadas de coleta desativadas, sem apps
  promovidos/sugestões, Otimização de Entrega sem P2P, limpeza de temporários e cache
  do Windows Update.
- **Windows 11**: desativa Widgets (processo Edge/WebView permanente), Copilot e
  Chat/Teams da barra; restaura o menu de contexto clássico (mais rápido em CPU
  fraca); remove bloatware que estreia no 11 (Clipchamp, Teams pessoal, To Do, Power
  Automate, Dev Home...); opcionalmente (Ultra, desmarcado por padrão e com aviso)
  desativa a Integridade de Memória (VBS/HVCI), que custa 5–15% de CPU.

**O que o programa NÃO faz:** não desativa Windows Update, não desativa o Windows
Defender, não mexe em drivers nem overclock.

Fontes consultadas:
[SysMain em HDD/low-RAM](https://www.softwarehubs.com/troubleshooting/sysmain-in-windows.html) ·
[SysMain: otimizar ou desativar](https://windowsforum.com/threads/sysmain-superfetch-in-windows-optimize-or-disable-for-better-performance.363969/) ·
[Serviços seguros de desativar](https://gist.github.com/Aldaviva/0eb62993639da319dc456cc01efa3fe5) ·
[6 serviços em segundo plano](https://windowsforum.com/threads/speed-up-windows-by-safely-disabling-6-background-services.383248/) ·
[Reduzir uso de RAM no Win10](https://bsr-studios.github.io/articles/reduce-ram-usage-windows-10.html)

## Estrutura do código

| Arquivo | Papel |
|---|---|
| `src/HardwareInfo.cs` | Detecção via WMI + recomendação de perfil |
| `src/Catalog.cs` | Catálogo das otimizações (condições de hardware, tiers, avisos) |
| `src/Actions.cs` | Ações atômicas: registro, serviços, comandos, tarefas, Appx, limpeza |
| `src/Engine.cs` | Aplicação, ponto de restauração, arquivo de reversão (undo) |
| `src/MainForm.cs` | Assistente em 5 passos |
| `src/SplashForm.cs` | Abertura de 5 s com fade in/out, logo e site clicável |
| `src/Diagnostics.cs` | Teste de RAM, saúde do disco (SMART) e benchmark com nota 1–10 |
| `app.manifest` | Elevação de administrador + DPI aware |
| `assets/logo.png` | Logo embutido no .exe como recurso |

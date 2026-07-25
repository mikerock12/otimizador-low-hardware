using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace OtimizadorWin10
{
    // ---------------------------------------------------------------
    // Catalogo de otimizacoes.
    // Tier: 1=Leve (sem perda funcional perceptivel), 2=Master
    // (desativa recursos que a maioria nao usa), 3=Ultra (agressivo,
    // maximo desempenho em troca de recursos secundarios).
    // Condicoes de hardware (HDD/SSD/RAM) decidem o que e pertinente:
    // ex. SysMain e indexacao sao criticos em HDD, irrelevantes ou
    // ate uteis em SSD com bastante RAM.
    // ---------------------------------------------------------------
    public static class Catalog
    {
        const string CAT_SERV = "Servicos do Windows";
        const string CAT_PRIV = "Privacidade e telemetria";
        const string CAT_UI = "Aparencia e interface";
        const string CAT_APPS = "Apps nativos";
        const string CAT_SIS = "Sistema e memoria";
        const string CAT_DISCO = "Disco e armazenamento";
        const string CAT_LIMP = "Limpeza";

        static Func<HardwareInfo, bool> SoHDD = delegate(HardwareInfo h) { return h.Disco != DiskKind.SSD; };
        static Func<HardwareInfo, bool> SoSSD = delegate(HardwareInfo h) { return h.Disco == DiskKind.SSD; };
        static Func<HardwareInfo, bool> PoucaRam = delegate(HardwareInfo h) { return h.RamMB <= 4608; };
        static Func<HardwareInfo, bool> SSDePoucaRam = delegate(HardwareInfo h) { return h.Disco == DiskKind.SSD && h.RamMB <= 4608; };
        static Func<HardwareInfo, bool> SoWin10 = delegate(HardwareInfo h) { return !h.EhWindows11; };
        static Func<HardwareInfo, bool> SoWin11 = delegate(HardwareInfo h) { return h.EhWindows11; };

        public static List<Optimization> Construir(HardwareInfo hw)
        {
            var lista = new List<Optimization>();

            // ===================== SERVICOS =====================
            lista.Add(Svc("svc-sysmain-hdd", "SysMain", "SysMain (Superfetch)",
                "Desativar SysMain/Superfetch (essencial em HDD)",
                "Em discos mecanicos (HDD) o SysMain mantem o disco em 100% de uso por minutos apos ligar, tentando pre-carregar programas. E a causa numero 1 de lentidao no Windows 10 em maquinas antigas com HDD.",
                1, SoHDD, null, false));

            lista.Add(Svc("svc-sysmain-ssd", "SysMain", "SysMain (Superfetch)",
                "Desativar SysMain/Superfetch",
                "Em SSD o SysMain pesa pouco no disco, mas em maquinas com 4 GB de RAM ou menos o pre-carregamento dele compete com seus programas pela memoria.",
                3, SSDePoucaRam, null, false));

            lista.Add(Svc("svc-wsearch-hdd", "WSearch", "Windows Search",
                "Desativar indexacao de pesquisa (Windows Search)",
                "O indexador de pesquisa gera leitura/gravacao constante no disco. Em HDD isso disputa o disco com tudo que voce faz. A pesquisa do menu Iniciar continua funcionando, apenas fica mais lenta ao procurar arquivos.",
                2, SoHDD, "A pesquisa de arquivos no Explorer e no menu Iniciar ficara mais lenta (mas continua funcionando).", false));

            lista.Add(Svc("svc-wsearch-ssd", "WSearch", "Windows Search",
                "Desativar indexacao de pesquisa (Windows Search)",
                "Em SSD a indexacao pesa menos, mas ainda consome RAM e CPU em segundo plano. So vale desativar na otimizacao maxima.",
                3, SoSSD, "A pesquisa de arquivos ficara mais lenta (mas continua funcionando).", false));

            lista.Add(Svc("svc-diagtrack", "DiagTrack", "Telemetria (DiagTrack)",
                "Desativar servico de telemetria (DiagTrack)",
                "O servico 'Experiencias do Usuario Conectado e Telemetria' coleta e envia dados de uso para a Microsoft, consumindo CPU, disco e rede em segundo plano.",
                1, null, null, false));

            lista.Add(Svc("svc-dmwappush", "dmwappushservice", "dmwappushservice",
                "Desativar servico de mensagens push WAP",
                "Servico auxiliar de telemetria/mensagens de operadora. Sem uso pratico em um computador.",
                1, null, null, false));

            var doSvc = new Optimization();
            doSvc.Id = "svc-dosvc"; doSvc.Categoria = CAT_SERV; doSvc.Tier = 1;
            doSvc.Nome = "Limitar Otimizacao de Entrega (updates P2P)";
            doSvc.Descricao = "Por padrao o Windows baixa e ENVIA atualizacoes para outros computadores pela internet, consumindo banda e disco. Isto limita o download ao modo direto e deixa o servico como manual.";
            doSvc.Acoes.Add(new RegAction("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization",
                "DODownloadMode", 0, RegistryValueKind.DWord, "Modo de download: apenas direto da Microsoft (sem P2P)"));
            doSvc.Acoes.Add(new ServiceAction("DoSvc", 3, "Otimizacao de Entrega"));
            lista.Add(doSvc);

            lista.Add(Svc("svc-fax", "Fax", "Fax", "Desativar servico de Fax",
                "Servico de fax. Praticamente ninguem usa fax em um notebook.", 2, null, null, false));

            lista.Add(Svc("svc-remotereg", "RemoteRegistry", "Registro Remoto",
                "Desativar Registro Remoto",
                "Permite que outros computadores alterem o registro desta maquina remotamente. Desativar tambem melhora a seguranca.", 2, null, null, false));

            lista.Add(Svc("svc-maps", "MapsBroker", "Gerenciador de Mapas",
                "Desativar gerenciador de mapas baixados",
                "Servico do app Mapas do Windows. Inutil se voce nao usa mapas offline da Microsoft.", 2, null, null, false));

            lista.Add(Svc("svc-wersvc", "WerSvc", "Relatorio de Erros",
                "Desativar Relatorio de Erros do Windows",
                "Coleta e envia relatorios de travamento para a Microsoft. Em maquinas lentas, a coleta apos um travamento piora ainda mais a situacao.", 2, null, null, false));

            lista.Add(Svc("svc-phone", "PhoneSvc", "Servico de Telefonia",
                "Desativar Servico de Telefone",
                "Da suporte ao app 'Seu Telefone' e chamadas. Inutil se voce nao vincula o celular ao PC.", 2, null, null, false));

            lista.Add(Svc("svc-retail", "RetailDemo", "Modo Demonstracao",
                "Desativar modo demonstracao de loja",
                "Servico usado apenas em computadores de vitrine de loja.", 2, null, null, false));

            lista.Add(Svc("svc-wmp", "WMPNetworkSvc", "Compart. Windows Media Player",
                "Desativar compartilhamento de midia do WMP",
                "Compartilha bibliotecas do Windows Media Player na rede. Raramente usado hoje.", 2, null, null, false));

            lista.Add(Svc("svc-geo", "lfsvc", "Servico de Geolocalizacao",
                "Desativar servico de geolocalizacao",
                "Fornece localizacao do dispositivo a apps. Notebooks antigos nem tem GPS; a localizacao por Wi-Fi e pouco usada.",
                2, null, "Apps que dependem de localizacao (ex.: Clima, Mapas) deixam de obter sua posicao automaticamente.", false));

            lista.Add(Svc("svc-tablet", "TabletInputService", "Teclado Virtual/Caneta",
                "Desativar servico de teclado virtual e caneta",
                "Da suporte a telas sensiveis ao toque e canetas. Inutil em notebooks sem tela touch.",
                2, null, "Se a tela for touch ou voce usar o teclado virtual, mantenha este servico.", false));

            lista.Add(Svc("svc-wbio", "WbioSrvc", "Biometria",
                "Desativar servico de biometria",
                "Da suporte a leitores de digital e reconhecimento facial. Notebooks antigos como Acer E1-531 e Samsung RV410 nao possuem esses sensores.",
                2, null, "Se voce usa leitor de impressao digital, mantenha este servico.", false));

            lista.Add(Svc("svc-wisvc", "wisvc", "Programa Windows Insider",
                "Desativar servico do Windows Insider",
                "So e necessario para quem participa do programa de testes do Windows.", 2, null, null, false));

            // Xbox
            var xbox = new Optimization();
            xbox.Id = "svc-xbox"; xbox.Categoria = CAT_SERV; xbox.Tier = 2;
            xbox.Nome = "Desativar servicos do Xbox";
            xbox.Descricao = "Quatro servicos de integracao com Xbox Live rodam em segundo plano mesmo sem nenhum jogo instalado. Em maquinas antigas sem uso de jogos da Microsoft, sao peso morto.";
            xbox.Aviso = "Se voce usa o app Xbox ou jogos da Microsoft Store com login Xbox Live, mantenha estes servicos.";
            xbox.Acoes.Add(new ServiceAction("XblAuthManager", 4, "Gerenciador de Autenticacao Xbox Live"));
            xbox.Acoes.Add(new ServiceAction("XblGameSave", 4, "Salvamento de Jogo do Xbox Live"));
            xbox.Acoes.Add(new ServiceAction("XboxNetApiSvc", 4, "Servico de Rede Xbox Live"));
            xbox.Acoes.Add(new ServiceAction("XboxGipSvc", 4, "Xbox Accessory Management"));
            lista.Add(xbox);

            var spooler = Svc("svc-spooler", "Spooler", "Spooler de Impressao",
                "Desativar fila de impressao (se nao usa impressora)",
                "O spooler de impressao consome memoria o tempo todo. Se este computador nunca imprime, pode ser desativado.",
                3, null, "Voce NAO conseguira imprimir nem instalar impressoras enquanto este servico estiver desativado.", true);
            lista.Add(spooler);

            lista.Add(Svc("svc-trkwks", "TrkWks", "Cliente de Rastreamento de Link",
                "Desativar rastreamento de links distribuidos",
                "Rastreia atalhos para arquivos movidos entre computadores da rede. Sem utilidade em uso domestico.", 3, null, null, false));

            lista.Add(Svc("svc-dps", "DPS", "Servico de Politica de Diagnostico",
                "Desativar diagnosticos automaticos",
                "Monitora e diagnostica problemas em segundo plano. Economiza RAM/CPU, mas os solucionadores de problemas do Windows param de funcionar.",
                3, null, "As ferramentas de 'Solucionar problemas' do Windows deixam de funcionar ate reativar.", true));

            // ===================== PRIVACIDADE / TELEMETRIA =====================
            var telReg = new Optimization();
            telReg.Id = "priv-telemetria"; telReg.Categoria = CAT_PRIV; telReg.Tier = 1;
            telReg.Nome = "Reduzir telemetria ao minimo";
            telReg.Descricao = "Define o nivel de dados de diagnostico enviados a Microsoft para o minimo permitido pelo Windows 10 Pro, reduzindo processamento e trafego em segundo plano.";
            telReg.Acoes.Add(new RegAction("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                "AllowTelemetry", 0, RegistryValueKind.DWord, "Politica de telemetria: nivel minimo (Seguranca/Basico)"));
            lista.Add(telReg);

            var telTasks = new Optimization();
            telTasks.Id = "priv-tarefas"; telTasks.Categoria = CAT_PRIV; telTasks.Tier = 1;
            telTasks.Nome = "Desativar tarefas agendadas de coleta de dados";
            telTasks.Descricao = "O Windows agenda varias tarefas de avaliacao de compatibilidade e coleta de dados que rodam sozinhas e ocupam disco/CPU nos piores momentos. Elas serao desativadas (podem ser reativadas na reversao).";
            telTasks.Acoes.Add(new TaskAction(@"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser"));
            telTasks.Acoes.Add(new TaskAction(@"\Microsoft\Windows\Application Experience\ProgramDataUpdater"));
            telTasks.Acoes.Add(new TaskAction(@"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator"));
            telTasks.Acoes.Add(new TaskAction(@"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip"));
            telTasks.Acoes.Add(new TaskAction(@"\Microsoft\Windows\Feedback\Siuf\DmClient"));
            telTasks.Acoes.Add(new TaskAction(@"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload"));
            telTasks.Acoes.Add(new TaskAction(@"\Microsoft\Windows\Autochk\Proxy"));
            lista.Add(telTasks);

            var sugestoes = new Optimization();
            sugestoes.Id = "priv-sugestoes"; sugestoes.Categoria = CAT_PRIV; sugestoes.Tier = 1;
            sugestoes.Nome = "Desativar sugestoes, dicas e apps promovidos";
            sugestoes.Descricao = "Impede que o Windows baixe e instale sozinho apps promovidos (Candy Crush etc.), mostre dicas e anuncios no menu Iniciar, na tela de bloqueio e nas notificacoes. Em maquinas lentas, essas instalacoes automaticas roubam disco e banda.";
            string cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
            sugestoes.Acoes.Add(new RegAction("HKCU", cdm, "ContentDeliveryAllowed", 0, RegistryValueKind.DWord, "Desligar entrega de conteudo promocional"));
            sugestoes.Acoes.Add(new RegAction("HKCU", cdm, "SilentInstalledAppsEnabled", 0, RegistryValueKind.DWord, "Impedir instalacao silenciosa de apps promovidos"));
            sugestoes.Acoes.Add(new RegAction("HKCU", cdm, "PreInstalledAppsEnabled", 0, RegistryValueKind.DWord, "Desligar apps pre-instalados promocionais"));
            sugestoes.Acoes.Add(new RegAction("HKCU", cdm, "OemPreInstalledAppsEnabled", 0, RegistryValueKind.DWord, "Desligar apps promocionais de fabricante"));
            sugestoes.Acoes.Add(new RegAction("HKCU", cdm, "SubscribedContent-338388Enabled", 0, RegistryValueKind.DWord, "Remover sugestoes no menu Iniciar"));
            sugestoes.Acoes.Add(new RegAction("HKCU", cdm, "SubscribedContent-338389Enabled", 0, RegistryValueKind.DWord, "Desligar dicas e truques em notificacoes"));
            sugestoes.Acoes.Add(new RegAction("HKCU", cdm, "SubscribedContent-353696Enabled", 0, RegistryValueKind.DWord, "Desligar sugestoes em Configuracoes"));
            sugestoes.Acoes.Add(new RegAction("HKCU", cdm, "SystemPaneSuggestionsEnabled", 0, RegistryValueKind.DWord, "Desligar sugestoes do sistema"));
            sugestoes.Acoes.Add(new RegAction("HKCU", cdm, "SoftLandingEnabled", 0, RegistryValueKind.DWord, "Desligar dicas do Windows"));
            lista.Add(sugestoes);

            var bgApps = new Optimization();
            bgApps.Id = "priv-bgapps"; bgApps.Categoria = CAT_PRIV; bgApps.Tier = 1;
            bgApps.Nome = "Desativar apps em segundo plano";
            bgApps.Descricao = "Impede que apps da Microsoft Store (Clima, Noticias, Fotos etc.) continuem rodando e consumindo RAM/CPU quando voce nao os esta usando. Um dos maiores ganhos de RAM em maquinas com 2-4 GB.";
            bgApps.Aviso = "Apps da Store nao atualizarao conteudo em segundo plano (ex.: notificacoes de apps da Store).";
            bgApps.Acoes.Add(new RegAction("HKCU", @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications",
                "GlobalUserDisabled", 1, RegistryValueKind.DWord, "Bloquear execucao de apps da Store em segundo plano"));
            lista.Add(bgApps);

            var cortana = new Optimization();
            cortana.Id = "priv-cortana"; cortana.Categoria = CAT_PRIV; cortana.Tier = 2;
            cortana.Nome = "Desativar Cortana";
            cortana.Descricao = "A Cortana roda em segundo plano consumindo RAM mesmo sem nunca ser usada. A pesquisa normal do menu Iniciar continua funcionando.";
            cortana.Acoes.Add(new RegAction("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\Windows Search",
                "AllowCortana", 0, RegistryValueKind.DWord, "Politica: desativar Cortana"));
            cortana.Acoes.Add(new RegAction("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Search",
                "BingSearchEnabled", 0, RegistryValueKind.DWord, "Desativar resultados do Bing na pesquisa local"));
            cortana.Acoes.Add(new RegAction("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Search",
                "CortanaConsent", 0, RegistryValueKind.DWord, "Revogar consentimento da Cortana"));
            lista.Add(cortana);

            var wer = new Optimization();
            wer.Id = "priv-adid"; wer.Categoria = CAT_PRIV; wer.Tier = 2;
            wer.Nome = "Desativar ID de publicidade";
            wer.Descricao = "Desliga o identificador usado para propaganda personalizada entre apps.";
            wer.Acoes.Add(new RegAction("HKCU", @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
                "Enabled", 0, RegistryValueKind.DWord, "Desligar ID de publicidade"));
            lista.Add(wer);

            // ===================== APARENCIA / INTERFACE =====================
            var vfx = new Optimization();
            vfx.Id = "ui-efeitos"; vfx.Categoria = CAT_UI; vfx.Tier = 1;
            vfx.Nome = "Efeitos visuais: ajustar para melhor desempenho";
            vfx.Descricao = "Desliga animacoes de janelas, sombras e transicoes que pesam nos graficos integrados antigos (a suavizacao de fontes e mantida para o texto continuar legivel). E o classico 'Ajustar para obter melhor desempenho', aplicado automaticamente. Efeito completo apos sair e entrar na conta.";
            vfx.Acoes.Add(new RegAction("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
                "VisualFXSetting", 2, RegistryValueKind.DWord, "Modo 'melhor desempenho' nos efeitos visuais"));
            vfx.Acoes.Add(new RegAction("HKCU", @"Control Panel\Desktop", "DragFullWindows", "0", RegistryValueKind.String, "Nao desenhar conteudo da janela ao arrastar"));
            vfx.Acoes.Add(new RegAction("HKCU", @"Control Panel\Desktop", "FontSmoothing", "2", RegistryValueKind.String, "Manter suavizacao de fontes (ClearType)"));
            vfx.Acoes.Add(new RegAction("HKCU", @"Control Panel\Desktop\WindowMetrics", "MinAnimate", "0", RegistryValueKind.String, "Desligar animacao de minimizar/maximizar"));
            vfx.Acoes.Add(new RegAction("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAnimations", 0, RegistryValueKind.DWord, "Desligar animacoes da barra de tarefas"));
            vfx.Acoes.Add(new RegAction("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ListviewAlphaSelect", 0, RegistryValueKind.DWord, "Desligar retangulo de selecao translucido"));
            vfx.Acoes.Add(new RegAction("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ListviewShadow", 0, RegistryValueKind.DWord, "Desligar sombras de icones"));
            lista.Add(vfx);

            var transp = new Optimization();
            transp.Id = "ui-transparencia"; transp.Categoria = CAT_UI; transp.Tier = 1;
            transp.Nome = "Desativar transparencias";
            transp.Descricao = "Os efeitos de transparencia da barra de tarefas e menu Iniciar exigem composicao grafica constante - peso desnecessario em GPUs integradas antigas (HD Graphics de 2a/3a geracao).";
            transp.Acoes.Add(new RegAction("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "EnableTransparency", 0, RegistryValueKind.DWord, "Desligar transparencia do Windows"));
            lista.Add(transp);

            var menu = new Optimization();
            menu.Id = "ui-menudelay"; menu.Categoria = CAT_UI; menu.Tier = 1;
            menu.Nome = "Acelerar abertura de menus";
            menu.Descricao = "Reduz o atraso artificial de 400ms na abertura de menus para 100ms - o sistema parece responder mais rapido imediatamente.";
            menu.Acoes.Add(new RegAction("HKCU", @"Control Panel\Desktop", "MenuShowDelay", "100", RegistryValueKind.String, "Atraso de menus: 400ms -> 100ms"));
            lista.Add(menu);

            var feeds = new Optimization();
            feeds.Id = "ui-feeds"; feeds.Categoria = CAT_UI; feeds.Tier = 1;
            feeds.Aplicavel = SoWin10;
            feeds.Nome = "Desativar 'Noticias e interesses' da barra de tarefas";
            feeds.Descricao = "O widget de noticias/clima na barra de tarefas mantem processos e consultas de rede em segundo plano e e conhecido por causar picos de CPU em maquinas fracas.";
            feeds.Acoes.Add(new RegAction("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\Windows Feeds",
                "EnableFeeds", 0, RegistryValueKind.DWord, "Politica: desativar Noticias e interesses"));
            lista.Add(feeds);

            // ===================== ESPECIFICOS DO WINDOWS 11 =====================
            var widgets = new Optimization();
            widgets.Id = "w11-widgets"; widgets.Categoria = CAT_UI; widgets.Tier = 1;
            widgets.Aplicavel = SoWin11;
            widgets.Nome = "Desativar Widgets (Windows 11)";
            widgets.Descricao = "O painel de Widgets do Windows 11 mantem um processo baseado no Edge (widgets.exe/WebView) rodando o tempo todo, com consumo constante de RAM e rede. Em um Celeron/Pentium com 4 GB, e um dos maiores ganhos do Windows 11.";
            widgets.Acoes.Add(new RegAction("HKLM", @"SOFTWARE\Policies\Microsoft\Dsh",
                "AllowNewsAndInterests", 0, RegistryValueKind.DWord, "Politica: desativar Widgets"));
            // Builds novas do Win11 protegem o valor TaskbarDa contra escrita
            // (acesso negado ate para admin). A politica acima ja desativa os
            // Widgets sozinha, entao esta acao e apenas cosmetica/complementar.
            var taskbarDa = new RegAction("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "TaskbarDa", 0, RegistryValueKind.DWord,
                "Remover botao de Widgets da barra (se o Windows permitir; a politica acima ja os desativa)");
            taskbarDa.BestEffort = true;
            widgets.Acoes.Add(taskbarDa);
            lista.Add(widgets);

            var chat = new Optimization();
            chat.Id = "w11-chat"; chat.Categoria = CAT_UI; chat.Tier = 1;
            chat.Aplicavel = SoWin11;
            chat.Nome = "Remover Chat/Teams da barra de tarefas (Windows 11)";
            chat.Descricao = "O icone de Chat integra o Microsoft Teams pessoal e pre-carrega componentes dele em segundo plano.";
            chat.Acoes.Add(new RegAction("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\Windows Chat",
                "ChatIcon", 3, RegistryValueKind.DWord, "Politica: ocultar icone de Chat"));
            var taskbarMn = new RegAction("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "TaskbarMn", 0, RegistryValueKind.DWord,
                "Remover icone Chat da barra (se o Windows permitir; a politica acima ja o oculta)");
            taskbarMn.BestEffort = true;
            chat.Acoes.Add(taskbarMn);
            lista.Add(chat);

            var copilot = new Optimization();
            copilot.Id = "w11-copilot"; copilot.Categoria = CAT_UI; copilot.Tier = 1;
            copilot.Aplicavel = SoWin11;
            copilot.Nome = "Desativar Copilot (Windows 11)";
            copilot.Descricao = "O Copilot roda sobre o Edge/WebView e consome RAM mesmo minimizado. Em maquinas fracas nao ha recurso sobrando para assistente de IA.";
            copilot.Acoes.Add(new RegAction("HKCU", @"Software\Policies\Microsoft\Windows\WindowsCopilot",
                "TurnOffWindowsCopilot", 1, RegistryValueKind.DWord, "Politica: desativar Windows Copilot"));
            var btnCopilot = new RegAction("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "ShowCopilotButton", 0, RegistryValueKind.DWord,
                "Remover botao do Copilot da barra (se o Windows permitir; a politica acima ja o desativa)");
            btnCopilot.BestEffort = true;
            copilot.Acoes.Add(btnCopilot);
            lista.Add(copilot);

            var ctxMenu = new Optimization();
            ctxMenu.Id = "w11-contextmenu"; ctxMenu.Categoria = CAT_UI; ctxMenu.Tier = 2;
            ctxMenu.Aplicavel = SoWin11;
            ctxMenu.Nome = "Restaurar menu de contexto classico (Windows 11)";
            ctxMenu.Descricao = "O novo menu do botao direito do Windows 11 e visivelmente mais lento para abrir em CPUs fracas e ainda exige um clique extra em 'Mostrar mais opcoes'. Isto restaura o menu classico, instantaneo. Efeito apos reiniciar o Explorer ou o computador.";
            ctxMenu.Acoes.Add(new RegAction("HKCU",
                @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32",
                "", "", RegistryValueKind.String, "Ativar menu de contexto classico do Explorer"));
            lista.Add(ctxMenu);

            var searchHl = new Optimization();
            searchHl.Id = "w11-searchhl"; searchHl.Categoria = CAT_UI; searchHl.Tier = 1;
            searchHl.Nome = "Desativar destaques da pesquisa";
            searchHl.Descricao = "Remove o conteudo dinamico (imagens, tendencias da web) baixado para a caixa de pesquisa, reduzindo consultas de rede e processos em segundo plano.";
            searchHl.Acoes.Add(new RegAction("HKCU", @"Software\Microsoft\Windows\CurrentVersion\SearchSettings",
                "IsDynamicSearchBoxEnabled", 0, RegistryValueKind.DWord, "Desligar destaques da pesquisa"));
            lista.Add(searchHl);

            var vbs = new Optimization();
            vbs.Id = "w11-vbs"; vbs.Categoria = CAT_SIS; vbs.Tier = 3;
            vbs.Aplicavel = SoWin11;
            vbs.DesmarcadaPorPadrao = true;
            vbs.Nome = "Desativar Integridade de Memoria (VBS/HVCI)";
            vbs.Descricao = "O Windows 11 ativa por padrao a virtualizacao de seguranca (Integridade de Memoria), que custa de 5 a 15% de desempenho de CPU - custo pesado em um Celeron de 2 nucleos. Desativar recupera esse desempenho.";
            vbs.Aviso = "Este E um recurso de seguranca (protege o nucleo do sistema contra drivers maliciosos). So marque se o desempenho for prioridade absoluta. Pode ser reativado em Seguranca do Windows > Seguranca do dispositivo. Requer reiniciar.";
            vbs.Acoes.Add(new RegAction("HKLM",
                @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity",
                "Enabled", 0, RegistryValueKind.DWord, "Desativar Integridade de Memoria (HVCI)"));
            lista.Add(vbs);

            var gamedvr = new Optimization();
            gamedvr.Id = "ui-gamedvr"; gamedvr.Categoria = CAT_UI; gamedvr.Tier = 1;
            gamedvr.Nome = "Desativar Game DVR / Barra de Jogo";
            gamedvr.Descricao = "A gravacao de tela em segundo plano do Xbox Game Bar consome RAM e GPU mesmo fora de jogos. Em maquinas antigas nao ha hardware sobrando para isso.";
            gamedvr.Acoes.Add(new RegAction("HKCU", @"System\GameConfigStore", "GameDVR_Enabled", 0, RegistryValueKind.DWord, "Desligar Game DVR"));
            gamedvr.Acoes.Add(new RegAction("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0, RegistryValueKind.DWord, "Politica: proibir gravacao de jogos"));
            gamedvr.Acoes.Add(new RegAction("HKCU", @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0, RegistryValueKind.DWord, "Desligar captura de apps"));
            lista.Add(gamedvr);

            // ===================== SISTEMA / MEMORIA =====================
            var startupDelay = new Optimization();
            startupDelay.Id = "sis-startupdelay"; startupDelay.Categoria = CAT_SIS; startupDelay.Tier = 1;
            startupDelay.Nome = "Remover atraso artificial dos programas de inicializacao";
            startupDelay.Descricao = "O Windows espera ~10 segundos apos o logon para iniciar os programas da bandeja. Remover o atraso deixa a area de trabalho utilizavel mais cedo.";
            startupDelay.Acoes.Add(new RegAction("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize",
                "StartupDelayInMSec", 0, RegistryValueKind.DWord, "Atraso de inicializacao de programas: 0"));
            lista.Add(startupDelay);

            var svchost = new Optimization();
            svchost.Id = "sis-svchost"; svchost.Categoria = CAT_SIS; svchost.Tier = 2;
            svchost.Aplicavel = PoucaRam;
            svchost.Nome = "Agrupar processos de servico (economia de RAM)";
            svchost.Descricao = "Desde 2017 o Windows 10 separa cada servico em um processo svchost proprio quando ha mais de 3,5 GB de RAM - sao dezenas de processos extras. Agrupa-los de volta (comportamento de maquinas com pouca RAM) economiza 100-300 MB de memoria.";
            svchost.Acoes.Add(new RegAction("HKLM", @"SYSTEM\CurrentControlSet\Control",
                "SvcHostSplitThresholdInKB", 0x4000000, RegistryValueKind.DWord, "Agrupar servicos em menos processos svchost"));
            lista.Add(svchost);

            var onedriveRun = new Optimization();
            onedriveRun.Id = "sis-onedrive-start"; onedriveRun.Categoria = CAT_SIS; onedriveRun.Tier = 2;
            onedriveRun.Nome = "Impedir OneDrive de iniciar com o Windows";
            onedriveRun.Descricao = "O OneDrive inicia junto com o Windows e fica sincronizando em segundo plano. Isto apenas o remove da inicializacao - voce ainda pode abri-lo manualmente quando quiser.";
            onedriveRun.Aviso = "Seus arquivos do OneDrive deixarao de sincronizar automaticamente ate voce abrir o OneDrive.";
            onedriveRun.Acoes.Add(new RegDeleteAction("HKCU",
                @"Software\Microsoft\Windows\CurrentVersion\Run", "OneDrive",
                "Remover OneDrive da inicializacao automatica"));
            lista.Add(onedriveRun);

            var pagefile = new Optimization();
            pagefile.Id = "sis-pagefile"; pagefile.Categoria = CAT_SIS; pagefile.Tier = 2;
            pagefile.Aplicavel = SoHDD;
            int pfMB = Math.Min(6144, Math.Max(2048, (hw.RamMB / 1024) * 1536));
            if (hw.RamMB <= 2560) pfMB = 4096;
            pagefile.Nome = "Fixar tamanho do arquivo de paginacao";
            pagefile.Descricao = string.Format("Em HDD, o arquivo de paginacao com tamanho automatico cresce e encolhe, fragmentando o disco e causando travadas. Sera fixado em {0} MB (minimo = maximo), dimensionado para {1:0.0} GB de RAM. Requer reiniciar.", pfMB, hw.RamGB);
            pagefile.Acoes.Add(new RegAction("HKLM", @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                "PagingFiles", new string[] { string.Format(@"C:\pagefile.sys {0} {0}", pfMB) }, RegistryValueKind.MultiString,
                string.Format("Arquivo de paginacao fixo: C:\\pagefile.sys = {0} MB", pfMB)));
            lista.Add(pagefile);

            var hiber = new Optimization();
            hiber.Id = "sis-hibernacao"; hiber.Categoria = CAT_SIS; hiber.Tier = 3;
            hiber.DesmarcadaPorPadrao = hw.Disco != DiskKind.SSD; // em HDD o fast startup ajuda no boot
            hiber.Nome = "Desativar hibernacao (libera varios GB em disco)";
            hiber.Descricao = string.Format("Apaga o arquivo de hibernacao (hiberfil.sys, ~{0:0.0} GB nesta maquina). Util quando o disco esta cheio.", hw.RamGB * 0.4);
            hiber.Aviso = "Desativa tambem a Inicializacao Rapida. Em HDD isso pode deixar o BOOT mais lento - por isso vem desmarcada em HDD. A opcao de hibernar some do menu Desligar.";
            hiber.Acoes.Add(new CmdAction("powercfg.exe", "/hibernate off",
                "Desativar hibernacao e apagar hiberfil.sys", "powercfg.exe", "/hibernate on"));
            lista.Add(hiber);

            // ===================== ENERGIA =====================
            var power = new Optimization();
            power.Id = "sis-energia"; power.Categoria = CAT_SIS; power.Tier = 2;
            power.Nome = "Plano de energia: Alto desempenho";
            power.Descricao = "Ativa o plano 'Alto desempenho', que impede o processador de reduzir a frequencia agressivamente. Em CPUs fracas (Pentium/Celeron antigos) a resposta do sistema melhora de forma perceptivel.";
            power.Aviso = hw.TemBateria
                ? "Em notebook, na bateria, a autonomia diminui. Recomendado principalmente para uso na tomada."
                : null;
            power.Acoes.Add(new CmdAction("powercfg.exe", "/setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",
                "Ativar plano de energia 'Alto desempenho'",
                "powercfg.exe", "/setactive 381b4222-f694-41f0-9685-ff5bb260df2e"));
            lista.Add(power);

            // ===================== DISCO =====================
            var trim = new Optimization();
            trim.Id = "disco-trim"; trim.Categoria = CAT_DISCO; trim.Tier = 1;
            trim.Aplicavel = SoSSD;
            trim.Nome = "Garantir TRIM ativo no SSD";
            trim.Descricao = "O TRIM mantem o desempenho de gravacao do SSD ao longo do tempo. Normalmente ja vem ativo; isto garante que esteja, o que importa em SSDs instalados por upgrade.";
            trim.Acoes.Add(new CmdAction("fsutil.exe", "behavior set DisableDeleteNotify 0",
                "Ativar TRIM (DisableDeleteNotify = 0)"));
            lista.Add(trim);

            var prefetch = new Optimization();
            prefetch.Id = "disco-prefetch"; prefetch.Categoria = CAT_DISCO; prefetch.Tier = 3;
            prefetch.Aplicavel = SoSSD;
            prefetch.Nome = "Desativar Prefetch (somente SSD)";
            prefetch.Descricao = "O Prefetch acelera carregamentos em HDD, mas em SSD e desnecessario e gera gravacoes extras. Em HDD ele NAO deve ser desativado - por isso so aparece para SSD.";
            prefetch.Acoes.Add(new RegAction("HKLM", @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters",
                "EnablePrefetcher", 0, RegistryValueKind.DWord, "Desativar Prefetcher"));
            lista.Add(prefetch);

            // ===================== APPS NATIVOS =====================
            AddApp(lista, "Microsoft.BingNews", "Noticias (Bing News)", 2, false, null);
            AddApp(lista, "Microsoft.BingWeather", "Clima", 2, false, null);
            AddApp(lista, "Microsoft.GetHelp", "Obter Ajuda", 2, false, null);
            AddApp(lista, "Microsoft.Getstarted", "Dicas do Windows", 2, false, null);
            AddApp(lista, "Microsoft.WindowsFeedbackHub", "Hub de Feedback", 2, false, null);
            AddApp(lista, "Microsoft.MicrosoftSolitaireCollection", "Microsoft Solitaire", 2, false, null);
            AddApp(lista, "Microsoft.Microsoft3DViewer", "Visualizador 3D", 2, false, null);
            AddApp(lista, "Microsoft.MixedReality.Portal", "Portal de Realidade Misturada", 2, false, null);
            AddApp(lista, "Microsoft.SkypeApp", "Skype (versao Store)", 2, false, null);
            AddApp(lista, "Microsoft.MicrosoftOfficeHub", "Office Hub (propaganda do Office)", 2, false, null);
            AddApp(lista, "Microsoft.Wallet", "Microsoft Wallet", 2, false, null);
            AddApp(lista, "Microsoft.People", "Pessoas", 2, false, null);
            AddApp(lista, "Microsoft.ZuneMusic", "Groove Musica", 2, false, "Se voce ouve musica com o Groove, desmarque.");
            AddApp(lista, "Microsoft.ZuneVideo", "Filmes e TV", 2, false, null);
            AddApp(lista, "Microsoft.YourPhone", "Seu Telefone", 2, false, "Se voce vincula o celular ao PC, desmarque.");
            AddApp(lista, "Microsoft.549981C3F5F10", "Cortana (app)", 2, false, null);
            AddApp(lista, "Microsoft.WindowsMaps", "Mapas", 2, false, "Se voce usa mapas offline, desmarque.");
            AddApp(lista, "Microsoft.OneConnect", "Planos Moveis", 2, false, null);
            AddApp(lista, "Microsoft.Print3D", "Print 3D", 2, false, null);
            AddApp(lista, "Microsoft.Messaging", "Mensagens", 2, false, null);

            AddApp(lista, "Microsoft.XboxApp", "Xbox (app)", 3, false, "Necessario para jogos com Xbox Live.");
            AddApp(lista, "Microsoft.XboxGamingOverlay", "Xbox Game Bar", 3, false, null);
            AddApp(lista, "Microsoft.XboxGameOverlay", "Xbox Game Overlay", 3, false, null);
            AddApp(lista, "Microsoft.XboxIdentityProvider", "Provedor de Identidade Xbox", 3, false, "Necessario para login em jogos da Microsoft.");
            AddApp(lista, "Microsoft.XboxSpeechToTextOverlay", "Xbox Speech to Text", 3, false, null);
            AddApp(lista, "Microsoft.Xbox.TCUI", "Xbox TCUI", 3, false, null);
            AddApp(lista, "Microsoft.WindowsAlarms", "Alarmes e Relogio", 3, false, "Desmarque se voce usa alarmes/cronometro.");
            AddApp(lista, "Microsoft.WindowsSoundRecorder", "Gravador de Voz", 3, false, "Desmarque se voce grava audio.");
            AddApp(lista, "Microsoft.ScreenSketch", "Ferramenta de Captura (Sketch)", 3, true, "Se voce faz capturas de tela com Win+Shift+S, desmarque.");
            AddApp(lista, "Microsoft.MSPaint", "Paint 3D", 3, false, "E o Paint 3D; o Paint classico nao e removido.", SoWin10);
            AddApp(lista, "Microsoft.WindowsCamera", "Camera", 3, true, "Desmarque se voce usa a webcam com o app Camera.");
            AddApp(lista, "microsoft.windowscommunicationsapps", "Email e Calendario", 3, true, "Desmarque se voce usa o app Email do Windows. Contas e emails locais serao removidos do app.");

            // Bloatware que estreia no Windows 11
            AddApp(lista, "MicrosoftTeams", "Teams pessoal (Chat)", 2, false, null, SoWin11);
            AddApp(lista, "MSTeams", "Microsoft Teams (novo)", 2, false, "Desmarque se voce usa Teams para trabalho/escola.", SoWin11);
            AddApp(lista, "Clipchamp.Clipchamp", "Clipchamp (editor de video)", 2, false, null, SoWin11);
            AddApp(lista, "Microsoft.Todos", "Microsoft To Do", 2, false, "Desmarque se voce usa listas do To Do.", SoWin11);
            AddApp(lista, "Microsoft.PowerAutomateDesktop", "Power Automate", 2, false, null, SoWin11);
            AddApp(lista, "Microsoft.Windows.DevHome", "Dev Home", 2, false, null, SoWin11);
            AddApp(lista, "Microsoft.GamingApp", "Xbox (app novo)", 3, false, "Necessario para jogos do Game Pass/Microsoft Store.", SoWin11);
            AddApp(lista, "Microsoft.OutlookForWindows", "Novo Outlook", 3, true, "Desmarque se voce usa o novo Outlook para email.", SoWin11);
            AddApp(lista, "MicrosoftWindows.Client.WebExperience", "Widgets (pacote Web Experience)", 3, false, "Remove de vez o mecanismo dos Widgets (alem de desativa-los). O painel de Widgets deixa de existir.", SoWin11);

            var onedriveUn = new Optimization();
            onedriveUn.Id = "app-onedrive"; onedriveUn.Categoria = CAT_APPS; onedriveUn.Tier = 3;
            onedriveUn.DesmarcadaPorPadrao = true;
            onedriveUn.Nome = "Desinstalar OneDrive por completo";
            onedriveUn.Descricao = "Remove o cliente OneDrive do computador. Os arquivos ja baixados permanecem na pasta do usuario; nada e apagado da nuvem. Pode ser reinstalado baixando do site da Microsoft.";
            onedriveUn.Aviso = "A sincronizacao com a nuvem para completamente. So marque se voce nao usa OneDrive.";
            onedriveUn.Acoes.Add(new UninstallOneDriveAction());
            lista.Add(onedriveUn);

            // ===================== LIMPEZA =====================
            var temp = new Optimization();
            temp.Id = "limp-temp"; temp.Categoria = CAT_LIMP; temp.Tier = 1;
            temp.Nome = "Limpar arquivos temporarios";
            temp.Descricao = "Apaga arquivos das pastas temporarias do usuario e do sistema. Em discos pequenos e cheios, espaco livre e desempenho andam juntos (o Windows precisa de folga para paginacao e updates). Arquivos em uso sao ignorados automaticamente.";
            temp.Acoes.Add(new CleanAction(new string[] { "%TEMP%", @"%SystemRoot%\Temp" },
                "Limpar %TEMP% e C:\\Windows\\Temp (arquivos em uso sao pulados)"));
            lista.Add(temp);

            var wuCache = new Optimization();
            wuCache.Id = "limp-wucache"; wuCache.Categoria = CAT_LIMP; wuCache.Tier = 2;
            wuCache.Nome = "Limpar cache do Windows Update";
            wuCache.Descricao = "Apaga instaladores de atualizacoes ja aplicadas (pasta SoftwareDistribution\\Download), que costumam ocupar varios GB. O Windows baixa novamente o que precisar.";
            wuCache.Acoes.Add(new ServiceControlAction(new string[] { "wuauserv", "bits" }, false,
                "Parar servicos de update temporariamente"));
            wuCache.Acoes.Add(new CleanAction(new string[] { @"%SystemRoot%\SoftwareDistribution\Download" },
                "Limpar C:\\Windows\\SoftwareDistribution\\Download"));
            wuCache.Acoes.Add(new ServiceControlAction(new string[] { "bits", "wuauserv" }, true,
                "Reiniciar servicos de update"));
            lista.Add(wuCache);

            var thumbs = new Optimization();
            thumbs.Id = "limp-thumbs"; thumbs.Categoria = CAT_LIMP; thumbs.Tier = 2;
            thumbs.Nome = "Limpar cache de miniaturas";
            thumbs.Descricao = "Remove caches de miniaturas corrompidos/inchados do Explorer. Eles serao recriados conforme o uso.";
            thumbs.Acoes.Add(new CleanAction(new string[] { @"%LocalAppData%\Microsoft\Windows\Explorer" },
                "Limpar cache de miniaturas do Explorer"));
            lista.Add(thumbs);

            return lista;
        }

        // ---------- helpers ----------
        static Optimization Svc(string id, string service, string nomeServico, string titulo,
                                string descricao, int tier, Func<HardwareInfo, bool> cond,
                                string aviso, bool desmarcada)
        {
            var o = new Optimization();
            o.Id = id; o.Categoria = CAT_SERV; o.Nome = titulo; o.Descricao = descricao;
            o.Tier = tier; o.Aplicavel = cond; o.Aviso = aviso; o.DesmarcadaPorPadrao = desmarcada;
            o.Acoes.Add(new ServiceAction(service, 4, nomeServico));
            return o;
        }

        static void AddApp(List<Optimization> lista, string pkg, string nome, int tier,
                           bool desmarcada, string aviso, Func<HardwareInfo, bool> cond = null)
        {
            var o = new Optimization();
            o.Id = "app-" + pkg.ToLowerInvariant();
            o.Categoria = CAT_APPS;
            o.Nome = "Remover: " + nome;
            o.Descricao = string.Format("Desinstala o app nativo \"{0}\". Libera espaco em disco e evita processos/atualizacoes em segundo plano. Pode ser reinstalado gratuitamente pela Microsoft Store.", nome);
            o.Tier = tier; o.DesmarcadaPorPadrao = desmarcada; o.Aviso = aviso; o.Aplicavel = cond;
            o.Acoes.Add(new AppxAction(pkg, nome));
            lista.Add(o);
        }
    }
}

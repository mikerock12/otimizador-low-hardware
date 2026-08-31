# Otimizador Low Hardware

Ferramenta de bancada para Windows 10/11 que detecta o hardware do computador, recomenda um nível de otimização e aplica ajustes de forma **explicável, selecionável e reversível**.

![Tela principal](docs/screenshots/02-passo1-hardware.png)

## O problema

Computadores antigos ou de entrada não devem receber o mesmo conjunto de ajustes. HDD, SSD, quantidade de RAM, CPU e versão do Windows mudam o que faz sentido otimizar.

O programa analisa a máquina antes de recomendar qualquer perfil.

## Principais recursos

- detecção de CPU, RAM, armazenamento, bateria e versão real do Windows;
- identificação de HDD/SSD por múltiplas estratégias;
- perfis Leve, Master e Ultra;
- catálogo de otimizações condicionado ao hardware;
- seleção item a item antes de aplicar;
- captura do estado anterior para permitir reversão;
- ponto de restauração;
- diagnóstico de memória e disco;
- benchmark de CPU, RAM e armazenamento;
- relatório e nota de 1 a 10 para a máquina.

## Stack

C# 5 · .NET Framework 4.x · WinForms · WMI · Registry API · ServiceController · P/Invoke Win32

A limitação a C# 5 é deliberada: o projeto compila com o `csc.exe` disponível no próprio Windows, sem Visual Studio, SDK ou NuGet.

## Decisões de engenharia

### Windows 11 pelo número de build

O programa não depende apenas de `ProductName`. A versão é inferida pelo build (`>= 22000`) para evitar identificar Windows 11 como Windows 10.

### Benchmark de disco sem cache

A medição de leitura 4K usa `CreateFileW` com `FILE_FLAG_NO_BUFFERING`. O buffer é alocado com alinhamento adequado via `VirtualAlloc`, evitando que o cache de páginas transforme o teste de disco em teste de RAM.

### Reversão baseada no estado real

Cada ação sabe capturar o estado anterior imediatamente antes da alteração. O arquivo de undo registra esse estado e a reversão executa as ações em ordem inversa.

### Catálogo declarativo

As condições de aplicabilidade pertencem aos próprios itens do catálogo. A interface apenas filtra o que é compatível com o hardware detectado.

## Fluxo

1. detectar hardware;
2. recomendar perfil;
3. revisar e personalizar ajustes;
4. aplicar com log e captura de undo;
5. executar diagnóstico e gerar relatório.

| Perfil | Aplicação |
| --- | --- |
| ![Perfil](docs/screenshots/03-passo2-perfil.png) | ![Aplicação](docs/screenshots/05-passo4-aplicar.png) |

## Como compilar

No Windows:

```bat
build.bat
```

O executável é gerado sem exigir Visual Studio ou instalação de SDK.

## Segurança e transparência

O software altera serviços e configurações do Windows, portanto requer privilégios administrativos. O código é público justamente para permitir auditoria do que é aplicado e como cada alteração é revertida.

## Autor

**Maicon Nunes** — Smells Like Tech Informática  
[GitHub](https://github.com/mikerock12) · [Site](https://www.smellsliketech.com.br)

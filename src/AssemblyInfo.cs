using System.Reflection;
using System.Runtime.InteropServices;

// Metadados do executavel. O csc.exe converte estes atributos no bloco
// VERSIONINFO do Win32 (o que aparece em Propriedades > Detalhes).
// Um binario SEM esses dados e um dos sinais de maior peso nos motores
// heuristicos do Windows Defender e do SmartScreen: praticamente todo
// malware e compilado sem identificacao, e todo software legitimo tem.
[assembly: AssemblyTitle("Otimizador Low Hardware")]
[assembly: AssemblyDescription("Otimizador de Windows 10 e 11 para maquinas de baixo desempenho, com diagnostico de hardware e reversao completa das alteracoes.")]
[assembly: AssemblyProduct("Otimizador Low Hardware")]
[assembly: AssemblyCompany("Smells Like Tech Informatica")]
[assembly: AssemblyCopyright("Copyright (C) 2026 Maicon Nunes - Smells Like Tech Informatica")]
[assembly: AssemblyTrademark("Smells Like Tech Informatica - www.smellsliketech.com.br")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyCulture("")]

[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

[assembly: ComVisible(false)]

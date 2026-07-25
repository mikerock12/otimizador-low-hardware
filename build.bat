@echo off
rem Compila o Otimizador usando o csc.exe do .NET Framework 4.x,
rem presente em qualquer Windows 10 (nao precisa de Visual Studio).

setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe

if not exist bin mkdir bin

"%CSC%" /nologo /target:winexe /out:bin\OtimizadorWin10.exe ^
  /win32manifest:app.manifest ^
  /win32icon:icon.ico ^
  /resource:assets\logo.png,logo.png ^
  /r:System.Management.dll ^
  /r:System.Web.Extensions.dll ^
  /r:System.ServiceProcess.dll ^
  /recurse:src\*.cs

if %errorlevel% neq 0 (
  echo.
  echo *** FALHA NA COMPILACAO ***
  exit /b 1
)
echo.
echo OK: bin\OtimizadorWin10.exe gerado.

rem ------------------------------------------------------------------
rem Assinatura digital (Authenticode) - OPCIONAL.
rem So roda se o certificado estiver configurado nestas variaveis; caso
rem contrario o build termina normalmente, apenas sem assinar.
rem
rem   set OTIM_CERT_SUBJECT=Smells Like Tech Informatica
rem   (o certificado precisa estar instalado no repositorio de
rem    certificados do usuario, ou plugado no token USB do emissor)
rem
rem signtool.exe faz parte do Windows SDK. Se nao existir, o passo e
rem apenas ignorado com aviso.
rem ------------------------------------------------------------------
if "%OTIM_CERT_SUBJECT%"=="" goto :fim

set SIGNTOOL=
for %%p in (signtool.exe) do if not "%%~$PATH:p"=="" set SIGNTOOL=%%~$PATH:p
if "%SIGNTOOL%"=="" (
  for /f "delims=" %%f in ('dir /b /s "%ProgramFiles(x86)%\Windows Kits\10\bin\*\x64\signtool.exe" 2^>nul') do set SIGNTOOL=%%f
)
if "%SIGNTOOL%"=="" (
  echo AVISO: signtool.exe nao encontrado - executavel NAO assinado.
  goto :fim
)

echo Assinando com o certificado "%OTIM_CERT_SUBJECT%"...
"%SIGNTOOL%" sign /n "%OTIM_CERT_SUBJECT%" /fd SHA256 ^
  /tr http://timestamp.digicert.com /td SHA256 ^
  /d "Otimizador Low Hardware" ^
  /du "https://www.smellsliketech.com.br" ^
  bin\OtimizadorWin10.exe
if %errorlevel% neq 0 (
  echo AVISO: falha ao assinar - executavel gerado, porem NAO assinado.
) else (
  echo OK: executavel assinado digitalmente.
)

:fim
endlocal

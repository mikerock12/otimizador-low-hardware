using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using OtimizadorWin10;
class FakeTask : ITaskHandle { public bool Value; public bool Enabled { get {return Value;} set {Value=value;} } public void Dispose() {} }
class OutcomeAction : OptAction {
 readonly string result; public OutcomeAction(string value) {result=value;}
 public override string Describe(){return "Teste de resultado";}
 public override Dictionary<string,object> CaptureUndo(){return null;}
 public override void Apply(Action<string> log){AppxAction.InterpretResult("{\"status\":\""+result+"\",\"messages\":[\"simulado\"]}",log);}
}
class RegressionTests {
 static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
 static int Main() {
  string folder=Path.Combine(Path.GetTempPath(),"slt-regression-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(folder);
  try {
   bool failed=false;
   try {new CmdAction("cmd.exe","/d /c exit 7","simulated error").Apply(delegate{});} catch(InvalidOperationException){failed=true;}
   Check(failed,"Failed command must not be success");
   string command="[Console]::WriteLine(('x' * 100000)); [Console]::Error.WriteLine(('e' * 100000)); exit 0";
   new CmdAction("powershell.exe","-NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(command)),"large output").Apply(delegate{});
   var a=new Dictionary<string,object>{{"Kind","cmd"},{"Exe","cmd.exe"},{"Args","/d /c exit 7"},{"Detalhe","test"}};
   var b=new Dictionary<string,object>{{"Kind","unknown"}};
   string file=Path.Combine(folder,"undo.json");
   var save=typeof(Engine).GetMethod("SalvarReversao",BindingFlags.Static|BindingFlags.NonPublic);
   save.Invoke(null,new object[]{file,new List<Dictionary<string,object>>{b,a}});
   Check(!File.Exists(file+".tmp"),"Journal update must be atomic");
   var serializer=new JavaScriptSerializer();
   var journal=serializer.Deserialize<List<Dictionary<string,object>>>(File.ReadAllText(file));
   Check(Convert.ToString(journal[0]["Kind"])=="cmd","Undo order must be reversed");
   Check(Engine.Reverter(file,delegate{})==0 && File.Exists(file),"Failed undo must remain retryable");
   a["Args"]="/d /c exit 0";
   save.Invoke(null,new object[]{file,new List<Dictionary<string,object>>{a}});
   Check(Engine.Reverter(file,delegate{})==1 && File.Exists(file+".revertido"),"Successful retry must complete undo");
   // Aplicacao simulada: ausencia/protecao nunca entram no contador de sucesso.
   var opt=new Optimization {Nome="Resultados simulados"};
   foreach(string status in new string[]{"removed","absent","blocked","failed"}) opt.Acoes.Add(new OutcomeAction(status));
   var result=Engine.AplicarNaPasta(new List<Optimization>{opt},false,delegate{},delegate{},folder);
   Check(result.Sucesso==1 && result.Ignoradas==1 && result.Avisos==1 && result.Falhas==1,"Summary must distinguish success, absent, warning and failure");
   bool invalid=false;try{new AppxAction("x'; throw 'injection", "test").Apply(delegate{});}catch(ArgumentException){invalid=true;}
   Check(invalid,"Package input must be validated before starting PowerShell");
   Check(typeof(AppxAction).Assembly.GetManifestResourceStream("AppxRemoval.ps1")!=null,"Removal resource must be embedded");
   var task=new FakeTask {Value=false};
   var action=new TaskAction("test",delegate{return task;});
   var taskUndo=action.CaptureUndo();action.Apply(delegate{});task.Value=true;
   TaskAction.RevertWith(taskUndo,delegate{},delegate{return task;});
   Check(!task.Value,"An originally disabled task must stay disabled after undo");
   task.Value=true;taskUndo=action.CaptureUndo();action.Apply(delegate{});
   Check(!task.Value,"Task must be disabled");
   TaskAction.RevertWith(taskUndo,delegate{},delegate{return task;});
   Check(task.Value,"An originally enabled task must be restored");
   var missingTask=new TaskAction("missing",delegate{throw new COMException("missing",unchecked((int)0x80070002));});
   Check(missingTask.CaptureUndo()==null,"Missing task must have no fake undo");
   bool skipped=false;try{missingTask.Apply(delegate{});}catch(ActionSkippedException){skipped=true;}
   Check(skipped,"Only genuinely missing tasks may be skipped");
   bool denied=false;try{new TaskAction("denied",delegate{throw new COMException("denied",unchecked((int)0x80070005));}).Apply(delegate{});}catch(COMException){denied=true;}
   Check(denied,"Task access denied must not be swallowed as missing");
   bool accountFailure=false;try{new TaskAction("account",delegate{throw new COMException("account",unchecked((int)0x8004130F));}).Apply(delegate{});}catch(COMException){accountFailure=true;}
   Check(accountFailure,"Scheduler account information error must not be treated as missing task");
   string cache=Path.Combine(folder,"cache");Directory.CreateDirectory(cache);
   File.WriteAllText(Path.Combine(cache,"thumbcache_1.db"),"cache");File.WriteAllText(Path.Combine(cache,"keep-important.txt"),"keep");
   new CleanAction(new string[]{cache},"Thumbnail test","thumbcache_*.db").Apply(delegate{});
   Check(!File.Exists(Path.Combine(cache,"thumbcache_1.db")) && File.Exists(Path.Combine(cache,"keep-important.txt")),"Thumbnail cleanup must preserve unrelated files");
   string transport = AppxAction.RunPowerShell("[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false); [Console]::WriteLine('acao com \"aspas\" e C:\\teste\\; \u00e7\u00e3')");
   Check(transport.Trim() == "acao com \"aspas\" e C:\\teste\\; \u00e7\u00e3", "Literal PowerShell transport must preserve Unicode, quotes and backslashes");
   string routine;
   using(var stream=typeof(AppxAction).Assembly.GetManifestResourceStream("AppxRemoval.ps1"))using(var reader=new StreamReader(stream,Encoding.UTF8))routine=reader.ReadToEnd();
   string mock = "[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false); function Get-AppxPackage { param($Name,$PackageTypeFilter,[switch]$AllUsers,$User,$ErrorAction) return @() }; function Remove-AppxPackage {throw 'Nunca executar remocao real no teste'}; & { " + routine + "\n } -PackageName 'Microsoft.Test'";
   string payload=AppxAction.RunPowerShell(mock);
   Check(Convert.ToString(serializer.Deserialize<Dictionary<string,object>>(payload)["status"])=="absent", "Windows PowerShell must run exact resource with mocked cmdlets and parse JSON");
   Console.WriteLine("OK: 19 verificacoes de comandos, journal, resultados, tarefas e limpeza; sem otimizacoes reais.");return 0;
  } catch(Exception e) {Console.Error.WriteLine(e);return 1;}
  finally {Directory.Delete(folder,true);}
 }
}

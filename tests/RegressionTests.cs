using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using OtimizadorWin10;
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
   Console.WriteLine("OK: 6 command/journal checks; no real optimizations executed.");return 0;
  } catch(Exception e) {Console.Error.WriteLine(e);return 1;}
  finally {Directory.Delete(folder,true);}
 }
}

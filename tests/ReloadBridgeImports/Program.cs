using IronPython.Hosting;
var old = Python.CreateEngine();
try { old.Execute("import clr\nclr.AddReference('System.Diagnostics.Process')\nfrom System.Diagnostics import FileVersionInfo"); Console.WriteLine("Original import already available"); }
catch (Exception ex) { Console.WriteLine("Original failure reproduced: " + ex.Message); }
var engine = Python.CreateEngine();
var scope = engine.CreateScope(); scope.SetVariable("exe", args[1]);
var imports = string.Join("\n", File.ReadAllLines(args[0]).Where(line => line == "import clr" || line.StartsWith("clr.AddReference(") || line.StartsWith("from System")));
engine.Execute(imports, scope);
engine.Execute("version = FileVersionInfo.GetVersionInfo(exe).ProductVersion\nassert version\npid = Process.GetCurrentProcess().Id\nassert pid > 0\ninfo = ProcessStartInfo()", scope);
Console.WriteLine("PASS: installed pyRevit IronPython; .NET " + Environment.Version + "; manager " + scope.GetVariable<string>("version"));

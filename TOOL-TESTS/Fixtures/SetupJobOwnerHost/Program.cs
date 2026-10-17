using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

// Test-only parent. Production Setup is loaded solely to exercise its real Job Object.
// No application startup, installer, model, account profile or network dependency is used.
if (args.Length != 4) return 2;
var setupAssemblyPath = Path.GetFullPath(args[0]);
var scriptPath = Path.GetFullPath(args[1]);
var readyPath = Path.GetFullPath(args[2]);
var receiptPath = Path.GetFullPath(args[3]);
var fixtureRoot = Path.GetDirectoryName(scriptPath)!;
if (!File.Exists(setupAssemblyPath) || !File.Exists(scriptPath) ||
    new[] { readyPath, receiptPath }.Any(path => Path.GetDirectoryName(path) != fixtureRoot)) return 3;
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    var candidate = Path.Combine(Path.GetDirectoryName(setupAssemblyPath)!, name.Name + ".dll");
    return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
};
var setupAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(setupAssemblyPath);
var jobType = setupAssembly.GetType("TOOL_SETUP.StaticBootstrapRunner+OwnedProcessJob", throwOnError: true)!;
using var job = (IDisposable)jobType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null)!;
using var child = Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"))
{
    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = fixtureRoot,
    Arguments = "/d /s /c \"\"" + scriptPath + "\"\""
}) ?? throw new InvalidOperationException("Could not create owned fixture process.");
jobType.GetMethod("Assign", BindingFlags.Public | BindingFlags.Instance)!.Invoke(job, [child]);
File.WriteAllText(readyPath, "job-assigned");
var temporary = receiptPath + ".part";
File.WriteAllText(temporary, JsonSerializer.Serialize(new { ownerPid = Environment.ProcessId, childPid = child.Id }));
File.Move(temporary, receiptPath);
Thread.Sleep(Timeout.Infinite);
return 0;

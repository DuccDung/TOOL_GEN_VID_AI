namespace TOOL_SETUP;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--inspect-shared-bootstrapper")
        {
            try
            {
                var options = InstallerOptions.Load();
                if (!options.IsSharedBootstrapper) throw new InvalidOperationException();
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(options.InspectSharedBootstrapper(),
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
                return 0;
            }
            catch { Console.WriteLine("{\"schemaVersion\":1,\"errorCode\":\"shared_bootstrapper_invalid\"}"); return 2; }
        }
        if (args.Length == 1 && args[0] == "--inspect-setup-binding")
        {
            try
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(InstallerOptions.Load().InspectBinding(),
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
                return 0;
            }
            catch { Console.WriteLine("{\"schemaVersion\":1,\"errorCode\":\"setup_binding_invalid\"}"); return 2; }
        }
        MaintenanceInvocation? maintenance = null;
        if (args.Length > 0)
        {
            try { maintenance = SetupMaintenanceHandoff.ParseArguments(args); }
            catch { Console.WriteLine("{\"schemaVersion\":1,\"errorCode\":\"setup_command_invalid\"}"); return 2; }
        }
        ApplicationConfiguration.Initialize();
        Application.Run(maintenance is null ? new InstallerForm() : new InstallerForm(null, null, false, maintenance));
        return 0;
    }
}

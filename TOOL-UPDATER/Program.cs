namespace TOOL_UPDATER;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            var options = UpdaterOptions.Parse(args);
            return new UpdaterService(options).Apply();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "VideoMaker Update",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }
}

using System.Reflection;
using System.Runtime.InteropServices;
using TOOL_LOCAL;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.Projects;

namespace TOOL_TESTS.Authentication;

[Collection(NativeWindowsCollection.Name)]
public sealed class BrandIdentityTests
{
    [Theory]
    [InlineData("dashboard")]
    [InlineData("login")]
    [InlineData("register")]
    [InlineData("forgot-password")]
    [InlineData("create-project")]
    public Task WindowsShellFallback_UsesBrandIconBeforeAndAfterHandleRecreation(string window) =>
        RunOnStaAsync(() =>
        {
            // Create handles without showing windows, starting WebView2 or contacting the server.
            using Form form = window switch
            {
                "dashboard" => new Form1(),
                "login" => new LoginForm(null!),
                "register" => new RegisterForm(null!),
                "forgot-password" => new ForgotPasswordForm(null!, null),
                _ => new CreateProjectForm()
            };
            Assert.Same(BrandIdentity.WindowIcon, form.Icon);
            Assert.True(form.ShowInTaskbar);
            AssertBrandIcons(form.Handle);

            var recreated = false;
            form.HandleCreated += (_, _) => recreated = true;
            typeof(Control).GetMethod("RecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(form, null);
            Assert.True(recreated);
            AssertBrandIcons(form.Handle);

            // If no per-window icon is available, the class must still carry the brand.
            SendMessage(form.Handle, 0x0080 /* WM_SETICON */, 1, 0);
            SendMessage(form.Handle, 0x0080, 0, 0);
            Assert.Equal(0, SendMessage(form.Handle, 0x007F /* WM_GETICON */, 1, 0));
            AssertClassIcons(form.Handle);
        });

    [Fact]
    public Task ExistingWindow_CanReceiveTheSameBrandIcon() => RunOnStaAsync(() =>
    {
        using var form = new Form();
        _ = form.Handle;
        BrandIdentity.ApplyWindowIcon(form);
        AssertBrandIcons(form.Handle);
    });

    private static void AssertBrandIcons(nint window)
    {
        AssertIconPixels(BrandIdentity.WindowIcon, SendMessage(window, 0x007F, 1, 0));
        Assert.NotEqual(0, SendMessage(window, 0x007F, 0, 0));
        AssertClassIcons(window);
    }

    private static void AssertClassIcons(nint window)
    {
        AssertIconPixels(BrandIdentity.WindowIcon, ClassIcon(window, -14 /* GCLP_HICON */));
        using var small = new Icon(BrandIdentity.WindowIcon, SystemInformation.SmallIconSize);
        AssertIconPixels(small, ClassIcon(window, -34 /* GCLP_HICONSM */));
    }

    private static void AssertIconPixels(Icon expected, nint actualHandle)
    {
        Assert.NotEqual(0, actualHandle);
        using var actual = Icon.FromHandle(actualHandle);
        using var expectedBitmap = expected.ToBitmap();
        using var actualBitmap = actual.ToBitmap();
        Assert.Equal(expectedBitmap.Size, actualBitmap.Size);
        for (var y = 0; y < expectedBitmap.Height; y++)
        for (var x = 0; x < expectedBitmap.Width; x++)
            Assert.Equal(expectedBitmap.GetPixel(x, y), actualBitmap.GetPixel(x, y));
    }

    private static nint ClassIcon(nint window, int index) => Environment.Is64BitProcess
        ? GetClassLongPtr(window, index)
        : (nint)GetClassLong(window, index);

    private static async Task RunOnStaAsync(Action test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { test(); completion.TrySetResult(); }
            catch (Exception error) { completion.TrySetException(error); }
        }) { IsBackground = true, Name = "Desktop brand icon test" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
        finally { Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "Icon test thread did not stop."); }
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern nint GetClassLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "GetClassLongW")]
    private static extern uint GetClassLong(nint window, int index);
}

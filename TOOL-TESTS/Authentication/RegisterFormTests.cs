using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TOOL_LOCAL;
using TOOL_LOCAL.Authentication;
using TOOL_SHARED.Contracts.Authentication;

namespace TOOL_TESTS.Authentication;

[Collection(NativeWindowsCollection.Name)]
public sealed class RegisterFormTests
{
    [Theory]
    [InlineData("json")]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("crypto")]
    [InlineData("network")]
    [InlineData("timeout")]
    [InlineData("unexpected")]
    public async Task RegistrationFailure_KeepsFormOpenAndRestoresControls(string failure) =>
        await RunOnStaAsync(async (form, scenario) =>
        {
            PrepareValidInput(form);
            var registration = form.SubmitRegistrationAsync();
            Assert.False(Field<AuthButton>(form, "_registerButton").Enabled);
            scenario.Response.SetException(failure switch
            {
                "json" => new JsonException("sensitive-test-detail"),
                "io" => new IOException("sensitive-test-detail"),
                "access" => new UnauthorizedAccessException("sensitive-test-detail"),
                "crypto" => new CryptographicException("sensitive-test-detail"),
                "network" => new HttpRequestException("sensitive-test-detail"),
                "timeout" => new TaskCanceledException("sensitive-test-detail"),
                _ => new InvalidOperationException("sensitive-test-detail")
            });

            await registration;

            Assert.True(form.Visible);
            Assert.False(scenario.Manager.IsAuthenticated);
            Assert.True(Field<AuthButton>(form, "_registerButton").Enabled);
            Assert.False(form.UseWaitCursor);
            var status = Field<Label>(form, "_statusLabel").Text;
            Assert.NotEmpty(status);
            Assert.DoesNotContain("sensitive-test-detail", status);
        });

    [Fact]
    public async Task DuplicateSubmission_CreatesOneRequestAndAuthenticatesOnSuccess() =>
        await RunOnStaAsync(async (form, scenario) =>
        {
            PrepareValidInput(form);
            var registration = form.SubmitRegistrationAsync();
            await form.SubmitRegistrationAsync();
            Assert.Equal(1, scenario.RegisterCount);
            scenario.Response.SetResult(TokenResponse());

            await registration;

            Assert.True(scenario.Manager.IsAuthenticated);
            Assert.Equal(1, scenario.SaveCount);
            Assert.Equal(DialogResult.OK, form.DialogResult);
            Assert.False(form.Visible);
        });

    [Fact]
    public async Task StorageFailure_ShowsLoginGuidanceWithoutRepeatingRequest() =>
        await RunOnStaAsync(async (form, scenario) =>
        {
            PrepareValidInput(form);
            scenario.SaveFailure = new IOException("sensitive-test-detail");
            var registration = form.SubmitRegistrationAsync();
            scenario.Response.SetResult(TokenResponse());

            await registration;

            Assert.False(scenario.Manager.IsAuthenticated);
            Assert.True(form.Visible);
            Assert.True(Field<AuthButton>(form, "_registerButton").Enabled);
            Assert.Contains("Tài khoản đã được tạo", Field<Label>(form, "_statusLabel").Text);
            Assert.Equal(1, scenario.RegisterCount);
        });

    [Fact]
    public async Task ClosingForm_CancelsRequestAndIgnoresLateSuccess() =>
        await RunOnStaAsync(async (form, scenario) =>
        {
            PrepareValidInput(form);
            var registration = form.SubmitRegistrationAsync();
            form.Close();
            Assert.True(scenario.RequestCancellation.IsCancellationRequested);
            scenario.Response.SetResult(TokenResponse());

            await registration;

            Assert.False(scenario.Manager.IsAuthenticated);
            Assert.Equal(0, scenario.SaveCount);
            Assert.NotEqual(DialogResult.OK, form.DialogResult);
        });

    [Fact]
    public async Task DuplicateEmail_PreservesMessageAndTraceId() =>
        await RunOnStaAsync(async (form, scenario) =>
        {
            PrepareValidInput(form);
            var registration = form.SubmitRegistrationAsync();
            scenario.Response.SetException(new AccountClientException("email_already_exists",
                "Email này đã được sử dụng.", 409, traceId: "test-registration-trace"));

            await registration;

            var status = Field<Label>(form, "_statusLabel").Text;
            Assert.Contains("Email này đã được sử dụng", status);
            Assert.Contains("test-registration-trace", status);
            Assert.True(Field<AuthButton>(form, "_registerButton").Enabled);
            Assert.False(scenario.Manager.IsAuthenticated);
        });

    private static void PrepareValidInput(RegisterForm form)
    {
        Field<AuthTextBox>(form, "_displayNameField").Value = "Test User";
        Field<AuthTextBox>(form, "_emailField").Value = "user@example.test";
        Field<AuthTextBox>(form, "_passwordField").Value = "StrongPass1!";
        Field<AuthTextBox>(form, "_passwordConfirmationField").Value = "StrongPass1!";
        Field<CheckBox>(form, "_termsCheckBox").Checked = true;
    }

    private static T Field<T>(RegisterForm form, string name) where T : Control =>
        (T)typeof(RegisterForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

    private static async Task RunOnStaAsync(Func<RegisterForm, RegistrationScenario, Task> test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Form? activeHost = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var scenario = new RegistrationScenario();
                using var host = new Form
                {
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-2000, -2000),
                    ShowInTaskbar = false
                };
                activeHost = host;
                Exception? failure = null;
                host.Shown += async (_, _) =>
                {
                    using var form = new RegisterForm(scenario.Manager)
                    {
                        StartPosition = FormStartPosition.Manual,
                        Location = new Point(-2000, -2000),
                        ShowInTaskbar = false
                    };
                    try
                    {
                        form.Show(host);
                        await test(form, scenario);
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                    finally
                    {
                        form.Close();
                        host.Close();
                    }
                };
                Application.Run(host);
                if (failure is not null) completion.TrySetException(failure);
                else completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }) { IsBackground = true, Name = "VideoMaker Registration Test" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try
        {
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            if (activeHost is { IsDisposed: false, IsHandleCreated: true })
                activeHost.BeginInvoke(new Action(activeHost.Close));
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "Registration UI thread did not stop.");
        }
    }

    private static AuthTokenResponse TokenResponse() => new("test-access", DateTime.UtcNow.AddHours(1),
        "test-refresh", DateTime.UtcNow.AddDays(1), Guid.NewGuid(), Guid.NewGuid(),
        new UserProfileResponse("user-test", "user@example.test", "Test User", "Active", ["User"]));

    private sealed class RegistrationScenario : IAccountApiClient, ITokenStore, IDisposable
    {
        private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"vm-register-form-{Guid.NewGuid():N}");
        public AccountSessionManager Manager { get; }
        public TaskCompletionSource<AuthTokenResponse> Response { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken RequestCancellation { get; private set; }
        public int RegisterCount { get; private set; }
        public int SaveCount { get; private set; }
        public Exception? SaveFailure { get; set; }

        public RegistrationScenario() => Manager = new AccountSessionManager(this, this,
            new DeviceIdentityService(Path.Combine(_workspace, "device-id.bin")));

        public Task<AuthTokenResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
        {
            RegisterCount++;
            RequestCancellation = cancellationToken;
            // Deliberately ignore cancellation to exercise a response arriving after the form closes.
            return Response.Task;
        }

        public Task<StoredRefreshToken?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<StoredRefreshToken?>(null);
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(StoredRefreshToken token, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return SaveFailure is null ? Task.CompletedTask : Task.FromException(SaveFailure);
        }

        public Task<AuthTokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AuthTokenResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task RequestPasswordResetAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task LogoutAsync(string accessToken, LogoutRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Dispose()
        {
            Manager.Dispose();
            if (Directory.Exists(_workspace)) Directory.Delete(_workspace, recursive: true);
        }
    }
}

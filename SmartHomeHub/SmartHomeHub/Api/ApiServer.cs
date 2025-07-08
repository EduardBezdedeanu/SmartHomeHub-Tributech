using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;
using System.Text.Json;

namespace SmartHomeHub.ApiServer
{
    public static class ApiServer
    {
        public static Task StartAsync(CancellationTokenSource cts)
        {
            var builder = WebApplication.CreateBuilder();

            var certPath = Path.Combine(AppContext.BaseDirectory, "cert", AppSecrets.Instance.PfxName);
         
            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                serverOptions.ListenAnyIP(5001, listenOptions =>
                {
                    listenOptions.UseHttps(certPath, AppSecrets.Instance.PfxPassword);
                });
            });

            var app = builder.Build();

            string validUser = AppSecrets.Instance.DeviceUser;
            string validPassword = AppSecrets.Instance.DevicePassword;
            string token = AppSecrets.Instance.ClientId.ToString(); // Return ClientId for as login token.(for test)
            bool started = false;

            //curl -X POST http://localhost:5000/api/login -H "Content-Type: application/json" -d "{\"Username\":\"device1\",\"Password\":\"meinSicheresPasswort\"}" --insecure
            app.MapPost("/api/login", async context =>
            {
                string validUser = AppSecrets.Instance.DeviceUser;
                string validPassword = AppSecrets.Instance.DevicePassword;

                var login = await JsonSerializer.DeserializeAsync<LoginRequest>(context.Request.Body);
                if (login?.Username == validUser && login?.Password == validPassword)
                {
                    await context.Response.WriteAsJsonAsync(new { token });
                }
                else
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsync("Unauthorized");
                }
            });

            app.MapPost("/api/start", async context =>
            {
                var auth = context.Request.Headers["Authorization"].ToString();
                if (auth != $"Bearer {token}")
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsync("Invalid token");
                    return;
                }

                started = true;
                await context.Response.WriteAsync("Device started");
            });

            app.MapPost("/api/stop", async context =>
            {
                var auth = context.Request.Headers["Authorization"].ToString();
                if (auth != $"Bearer {token}")
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsync("Invalid token");
                    return;
                }

                if (started)
                {
                    cts.Cancel();
                    started = false;
                    await context.Response.WriteAsync("Device stopped");
                }
                else
                {
                    await context.Response.WriteAsync("Device not running");
                }
            });

            return app.RunAsync(cts.Token);
        }

        private record LoginRequest(string Username, string Password);
    }

}

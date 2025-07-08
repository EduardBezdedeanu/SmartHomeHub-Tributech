using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Protocol;
using SmartHomeHub.ApiServer;
using SmartHomeHub.Mock;
class Program
{
    private static HttpClient CreateAuthenticatedHttpClient()
    {
        var handler = new HttpClientHandler();

        var certificates = StaticCertificateProvider.Instance.GetCertificates();

        if (certificates.Count == 0)
            throw new InvalidOperationException("Client certificate not found!");

        handler.ClientCertificates.Add(certificates[0]);

        handler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) =>
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            Console.WriteLine($"Certificate error: {sslPolicyErrors}");
            return false;
        };

        var httpClient = new HttpClient(new FakeHttpMessageHandler()); // new HttpClient(handler);

        var username = AppSecrets.Instance.HubUser;
        var password = AppSecrets.Instance.HubPassword;
        var base64 = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64);

        return httpClient;
    }
    private static async Task<HttpResponseMessage> SendHeartbeatAsync(HttpClient httpClient, CancellationToken token)
    {
        var response = await httpClient.GetAsync("https://hub.io/api/heartbeat", token);
        Console.WriteLine($"Response from SendHeartbeatAsync: {response.StatusCode}");

        var content = await response.Content.ReadAsStringAsync(token);
        Console.WriteLine(content);

        return response;
    }

    private static async Task<HttpResponseMessage> RegisterDeviceAsync(HttpClient httpClient, CancellationToken token)
    {
        var registrationData = new
        {
            deviceId = AppSecrets.Instance.ClientId, // Hardware Id
            model = "Sensor-Temperature",
            firmwareVersion = "1.0.0"
        };

        var jsonContent = JsonSerializer.Serialize(registrationData);
        using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync("https://hub.io/api/register", content, token);
        Console.WriteLine($"Registration: {response.StatusCode}");

        var responseBody = await response.Content.ReadAsStringAsync(token);
        Console.WriteLine(responseBody);

        return response;
    }

    private static async Task RunApplicationAsync(CancellationToken token)
    {
        var mqttTask = ConnectAndListenMqttAsync(token);
        var tempTask = SendTemperatureAsync(token);

        await Task.WhenAll(mqttTask, tempTask);
    }

    static async Task Main()
    {
        AppSecrets.Load(); // Load App Secrets

        var clientId = AppSecrets.Instance.ClientId;
        Console.WriteLine($"Client ID: {clientId}");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("Program terminated...");
        };

        _ = ApiServer.StartAsync(cts); // start REST-API in background

        try
        {
            var httpClient = CreateAuthenticatedHttpClient();

            var registerResponse = await RegisterDeviceAsync(httpClient, cts.Token);
                      
            if (registerResponse.StatusCode == HttpStatusCode.Created)
            {
                Console.WriteLine("New device sucessfully registred.");
            }
            else if (registerResponse.StatusCode == HttpStatusCode.OK)
            {
                Console.WriteLine("Device is already registered.");
            }
            else if (registerResponse.StatusCode == HttpStatusCode.Conflict)
            {
                Console.WriteLine("Device is already registered – Conflict.");
            }
            else
            {
                Console.WriteLine($"Unknown status: {registerResponse.StatusCode}");
                return;
            }

            var response = await SendHeartbeatAsync(httpClient, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                await RunApplicationAsync(cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Program canceled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }        
    }

    static async Task SendTemperatureAsync(CancellationToken cancellationToken)
    {
        var factory = new MqttClientFactory();
        var mqttClient = factory.CreateMqttClient();

        var options = new MqttClientOptionsBuilder()
        .WithTcpServer(AppSecrets.Instance.Host, 8883)
        .WithCredentials(AppSecrets.Instance.HubUser, AppSecrets.Instance.HubPassword)
        .WithClientId("device1-temp-sender")
        .WithCleanSession()
        .WithTlsOptions(tls =>
        {
            tls.UseTls();
            tls.WithSslProtocols(SslProtocols.Tls12);
            tls.WithCertificateValidationHandler(_ => true);
            tls.WithClientCertificatesProvider(new StaticCertificateProvider());           
        })
        .Build();
        try
        {
            await mqttClient.ConnectAsync(options, cancellationToken);
            Console.WriteLine("MQTT connected for transmission.");

            var random = new Random();

            while (!cancellationToken.IsCancellationRequested)
            {
                double temperature = random.NextDouble() * 10 + 20; // 20.0 bis 30.0

                var payload = $"{{ \"temperature\": {temperature.ToString("F1").Replace(",", ".")} }}";

                var message = new MqttApplicationMessageBuilder()
                    .WithTopic("home/sensor/temperature")
                    .WithPayload(payload)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
                    .WithRetainFlag()
                    .Build();

                await mqttClient.PublishAsync(message, cancellationToken);
                Console.WriteLine($"Temperature sent: {payload}");
                await Task.Delay(5000);
            }
            await mqttClient.DisconnectAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during MQTT transmission: {ex.Message}");
        }
        finally
        {
            await mqttClient.DisconnectAsync();
            Console.WriteLine("MQTT disconnected after transmission.");
        }
    }
    static async Task ConnectAndListenMqttAsync(CancellationToken cancellationToken)
    {
        var mqttFactory = new MqttClientFactory();
        var mqttClient = mqttFactory.CreateMqttClient();

        var mqttOptions = new MqttClientOptionsBuilder()
            .WithTcpServer(AppSecrets.Instance.Host, 8883)
            .WithCredentials(AppSecrets.Instance.HubUser, AppSecrets.Instance.HubPassword)
            .WithClientId(Guid.NewGuid().ToString())
            .WithCleanSession()
            .WithTlsOptions(tls =>
            {
                tls.UseTls();
                tls.WithSslProtocols(SslProtocols.Tls12);
                tls.WithCertificateValidationHandler(_ => true);

                //Get certificates via provider
                tls.WithClientCertificatesProvider(new StaticCertificateProvider());
            })
            .Build();

        mqttClient.ApplicationMessageReceivedAsync  += async e =>
        {
            var topic = e.ApplicationMessage.Topic;
            var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
            Console.WriteLine($"Message received on topic '{topic}': {payload}");

            if (topic == "device/update/firmware/raw")
            {
                Console.WriteLine("Firmware-Update (RAW) received via MQTT.");
                try
                {
                    byte[] firmwareBytes = Convert.FromBase64String(payload);
                    await File.WriteAllBytesAsync("firmware.bin", firmwareBytes, cancellationToken);
                    Console.WriteLine("Firmware saved to binary file 'firmware.bin'");                 
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during writting the Firmware: {ex.Message}");
                }
            }

            Console.WriteLine($"Message received: {Encoding.UTF8.GetString(e.ApplicationMessage.Payload)}");           
        };

        await mqttClient.ConnectAsync(mqttOptions, cancellationToken);
        Console.WriteLine("Connected with MQTT-Broker (Listening)");

        await mqttClient.SubscribeAsync("device/update/firmware/raw", MqttQualityOfServiceLevel.AtLeastOnce);
        Console.WriteLine("Subscribing to 'device/update/firmware/raw'");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(500, cancellationToken);
            }
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("MQTT-Loop terminated.");
        }

        await mqttClient.DisconnectAsync();
        Console.WriteLine("Connection to MQTT disconnected.");
    }
}

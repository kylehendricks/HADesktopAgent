using HAWindowsAgent.PowerState;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;
using System.Text;

namespace HAWindowsAgent.Mqtt
{
    public delegate void MqttConnectedHandler();
    public delegate void MqttMessageHandler(string topic, string payload);

    public class MqttManager
    {
        private const string HA_STATUS_ONLINE = "online";
        private const string HA_STATUS_OFFLINE = "offline";
        private const long PING_DELAY = 5;
        private const long CONNECT_TIMEOUT = 1;
        private const long RECONNECT_ATTEMPT_DELAY = 1;

        private readonly IMqttClient _mqttClient;
        private readonly CancellationToken _cancellationToken;
        private readonly IPowerState _powerState;
        private readonly ILogger<MqttManager> _logger;
        private readonly string _statusTopic;

        private bool _shouldStayConnected = true;

        public event MqttConnectedHandler? MqttConnected;
        public event MqttMessageHandler? MqttMessage;

        public bool IsConnected => _mqttClient?.IsConnected ?? false;
        public string StatusTopic => _statusTopic;

        public MqttManager(ILogger<MqttManager> logger, IPowerState powerState, IHostApplicationLifetime lifetime, IOptions<MqttConfiguration> mqttConfig)
        {
            _logger = logger;
            _cancellationToken = lifetime.ApplicationStopping;

            var config = mqttConfig.Value;
            _statusTopic = config.StatusTopic;

            var mqttFactory = new MqttClientFactory();
            _mqttClient = mqttFactory.CreateMqttClient();

            var builder = new MqttClientOptionsBuilder()
                .WithTcpServer(config.Host);

            if (!string.IsNullOrEmpty(config.Username) && !string.IsNullOrEmpty(config.Password))
            {
                builder.WithCredentials(config.Username, config.Password);
            }

            var mqttClientOptions = builder
                .WithWillPayload(HA_STATUS_OFFLINE)
                .WithWillTopic(config.StatusTopic)
                .WithWillRetain(true)
                .Build();

            _mqttClient.ApplicationMessageReceivedAsync += HandleMqttApplicationMessage;

            _powerState = powerState;
            _powerState.OnSuspend += PowerState_OnSuspend;
            _powerState.OnResume += PowerState_OnResume;

            _ = Task.Run(() => ReconnectionLoopAsync(mqttClientOptions), _cancellationToken);
        }

        public async Task PublishAsync(string topic, string payload, bool retain)
        {
            if (_mqttClient == null || !_mqttClient.IsConnected)
            {
                throw new InvalidOperationException("MQTT client is not connected");
            }

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithRetainFlag(retain)
                .Build();

            await _mqttClient.PublishAsync(message, _cancellationToken);
        }
        public async Task SubscribeAsync(string topic)
        {
            if (_mqttClient == null || !_mqttClient.IsConnected)
            {
                throw new InvalidOperationException("MQTT client is not connected");
            }

            await _mqttClient.SubscribeAsync(topic, cancellationToken: _cancellationToken);
        }

        public async Task UnsubscribeAsync(string topic)
        {
            if (_mqttClient == null || !_mqttClient.IsConnected)
            {
                throw new InvalidOperationException("MQTT client is not connected");
            }

            await _mqttClient.UnsubscribeAsync(topic, cancellationToken: _cancellationToken);
        }

        private async Task HandleMqttApplicationMessage(MqttApplicationMessageReceivedEventArgs e)
        {
            var handler = MqttMessage;
            if (handler == null) return;

            string payloadString;
            try
            {
                payloadString = Encoding.UTF8.GetString(e.ApplicationMessage.Payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode MQTT payload to UTF8");
                return;
            }

            // Invoke each subscriber on a background thread with individual exception handling
            foreach (var subscriber in handler.GetInvocationList())
            {
                await Task.Run(() =>
                {
                    try
                    {
                        ((MqttMessageHandler)subscriber).Invoke(e.ApplicationMessage.Topic, payloadString);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception in MqttMessage event handler");
                    }
                });
            }
        }
        private async Task ReconnectionLoopAsync(MqttClientOptions mqttClientOptions)
        {
            while (!_cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_shouldStayConnected && !await _mqttClient.TryPingAsync(_cancellationToken))
                    {
                        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(CONNECT_TIMEOUT));
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken, timeoutCts.Token);

                        await _mqttClient.ConnectAsync(mqttClientOptions, linkedCts.Token);

                        await PublishAsync(_statusTopic, HA_STATUS_ONLINE, true);

                        RaiseMqttConnectedAsync();
                        _logger.LogInformation("Connected to MQTT broker");
                    }

                }
                catch (OperationCanceledException)
                {
                    // Check if the main application is stopping
                    if (_cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogDebug("Application stopping, exiting MQTT reconnection loop");
                        break;
                    }
                    // Otherwise, it was the connect timeout - log and continue to retry
                    _logger.LogWarning("MQTT connection attempt timed out after {Timeout}s, will retry", CONNECT_TIMEOUT);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Error in reconnection loop: {Message}", ex.Message);
                }
                finally
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(_mqttClient.IsConnected ? PING_DELAY : RECONNECT_ATTEMPT_DELAY), _cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            }
        }

        private void RaiseMqttConnectedAsync()
        {
            var handler = MqttConnected;
            if (handler == null) return;

            foreach (var subscriber in handler.GetInvocationList())
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        ((MqttConnectedHandler)subscriber).Invoke();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception in MqttConnected event handler");
                    }
                });
            }
        }
        private void PowerState_OnResume()
        {
            _shouldStayConnected = true;
        }

        private async void PowerState_OnSuspend()
        {
            _shouldStayConnected = false;
            await PublishAsync(_statusTopic, HA_STATUS_OFFLINE, true);
            await _mqttClient.TryDisconnectAsync();
        }

        public void Dispose()
        {
            _powerState.OnResume -= PowerState_OnResume;
            _powerState.OnSuspend -= PowerState_OnSuspend;
            _mqttClient.ApplicationMessageReceivedAsync -= HandleMqttApplicationMessage;
            _mqttClient?.Dispose();
        }
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ShopAutoBuyer.Core.Utils;

public static class SocketBridgeClient
{
    private const string Host = "127.0.0.1";
    private const int Port = 9876;

    private static TcpClient? _client;
    private static NetworkStream? _stream;
    private static StreamWriter? _writer;
    private static StreamReader? _reader;
    private static CancellationTokenSource? _cts;
    private static readonly object _lock = new();
    private static bool _isRunning = false;
    private static Action<string>? _onMessageReceived;

    public static bool IsConnected
    {
        get
        {
            lock (_lock)
            {
                return _client != null && _client.Connected;
            }
        }
    }

    public static void Initialize(Action<string>? onMessageReceived = null)
    {
        if (_isRunning) return;
        _isRunning = true;
        _onMessageReceived = onMessageReceived;
        _cts = new CancellationTokenSource();

        Task.Run(async () => await ConnectionLoopAsync(_cts.Token));
    }

    private static async Task ConnectionLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _isRunning)
        {
            try
            {
                if (!IsConnected)
                {
                    var client = new TcpClient();
                    var connectTask = client.ConnectAsync(Host, Port);
                    var timeoutTask = Task.Delay(2000, ct);

                    if (await Task.WhenAny(connectTask, timeoutTask) == connectTask && client.Connected)
                    {
                        lock (_lock)
                        {
                            _client = client;
                            _stream = client.GetStream();
                            _writer = new StreamWriter(_stream, Encoding.UTF8) { AutoFlush = true };
                            _reader = new StreamReader(_stream, Encoding.UTF8);
                        }

                        LogHelper.Info("[SOCKET BRIDGE] Đã kết nối thành công tới Python Trade Server (127.0.0.1:9876)!");
                        _ = Task.Run(async () => await ReceiveLoopAsync(_reader, ct));
                    }
                    else
                    {
                        client.Dispose();
                    }
                }
            }
            catch
            {
                // Python server chưa bật hoặc đang restart, tiếp tục thử lại
            }

            try
            {
                await Task.Delay(2000, ct);
            }
            catch
            {
                break;
            }
        }
    }

    private static async Task ReceiveLoopAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _isRunning)
            {
                var line = await reader.ReadLineAsync();
                if (line == null) break; // Disconnected

                try
                {
                    _onMessageReceived?.Invoke(line);
                }
                catch (Exception ex)
                {
                    LogHelper.Warn($"[SOCKET BRIDGE] Lỗi xử lý tin nhắn từ Python: {ex.Message}");
                }
            }
        }
        catch
        {
            // Mất kết nối
        }
        finally
        {
            DisconnectInternal();
        }
    }

    public static void SendStatus(string status, int itemsBought = 0, List<string>? lastItems = null, string seller = "")
    {
        var payload = new
        {
            status = status,
            seller = seller,
            items_bought = itemsBought,
            last_items = lastItems ?? new List<string>(),
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        var json = JsonSerializer.Serialize(payload);

        lock (_lock)
        {
            if (_writer != null && IsConnected)
            {
                try
                {
                    _writer.WriteLine(json);
                }
                catch
                {
                    DisconnectInternal();
                }
            }
        }
    }

    private static void DisconnectInternal()
    {
        lock (_lock)
        {
            try { _writer?.Dispose(); } catch { }
            try { _reader?.Dispose(); } catch { }
            try { _stream?.Dispose(); } catch { }
            try { _client?.Dispose(); } catch { }
            _writer = null;
            _reader = null;
            _stream = null;
            _client = null;
        }
    }

    public static void Dispose()
    {
        _isRunning = false;
        _cts?.Cancel();
        DisconnectInternal();
    }
}

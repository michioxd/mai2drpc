using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using MelonLoader;

namespace Mai2DRPC.Discord
{
    internal sealed class DiscordIpcClient : IDisposable
    {
        public event Action? OnReady;
        public event Action<string>? OnError;

        private readonly string clientId;
        private readonly MelonLogger.Instance logger;
        private readonly int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
        private readonly long startTimeUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private NamedPipeClientStream? pipe;
        private readonly AutoResetEvent reconnectSignal = new AutoResetEvent(false);
        private int nonce;
        private volatile bool connected;
        private volatile bool disposed;
        private readonly object writeLock = new object();

        private const int ophandshake = 0;
        private const int opframe = 1;
        private const int opclose = 2;
        private const int reconnectdelayms = 5000;
        private const int maxbuffersize = 1048576;

        public bool IsReady => connected;

        public DiscordIpcClient(string clientId, MelonLogger.Instance logger)
        {
            this.clientId = clientId;
            this.logger = logger;
        }

        public void Start()
        {
            new Thread(connectLoop) { IsBackground = true, Name = "Mai2DRPC-Connect" }.Start();
        }

        public void SetActivity(
            string? details,
            string? state,
            string? largeImageKey = null,
            string? largeImageText = null,
            string? smallImageKey = null,
            string? smallImageText = null,
            string? buttonLabel = null,
            string? buttonUrl = null
        )
        {
            if (!connected || pipe == null || !pipe.IsConnected)
                return;

            int n = Interlocked.Increment(ref nonce);
            string activity = buildActivity(
                details,
                state,
                largeImageKey,
                largeImageText,
                smallImageKey,
                smallImageText,
                buttonLabel,
                buttonUrl
            );
            string json =
                $"{{\"cmd\":\"SET_ACTIVITY\",\"args\":{{\"pid\":{pid},\"activity\":{activity}}},\"nonce\":\"{n}\"}}";

            logger.Msg($"[IPC] Sending SET_ACTIVITY: {json}");
            writeFrame(opframe, json);
        }

        public void Dispose()
        {
            disposed = true;
            connected = false;
            closePipe();
            reconnectSignal.Set();
        }

        private void connectLoop()
        {
            while (!disposed)
            {
                if (pipe == null || !pipe.IsConnected)
                {
                    pipe = attemptConnect();
                    if (pipe != null)
                    {
                        string handshakeJson = $"{{\"v\":1,\"client_id\":\"{clientId}\"}}";

                        byte[] data = Encoding.UTF8.GetBytes(handshakeJson);
                        byte[] frame = new byte[8 + data.Length];
                        writeInt32LE(frame, 0, ophandshake);
                        writeInt32LE(frame, 4, data.Length);
                        Buffer.BlockCopy(data, 0, frame, 8, data.Length);

                        try
                        {
                            pipe.Write(frame, 0, frame.Length);
                            pipe.Flush();
                            logger.Msg("[IPC] Wrote Handshake");
                            beginReadHeader();
                        }
                        catch
                        {
                            closePipe();
                        }
                    }
                }

                reconnectSignal.WaitOne(pipe != null && pipe.IsConnected ? 100 : reconnectdelayms);
            }
        }

        private NamedPipeClientStream? attemptConnect()
        {
            for (int i = 0; i <= 9 && !disposed; i++)
            {
                try
                {
                    var p = new NamedPipeClientStream(
                        ".",
                        $"discord-ipc-{i}",
                        PipeDirection.InOut,
                        PipeOptions.Asynchronous
                    );
                    p.Connect(10);
                    logger.Msg($"[IPC] Connected to discord-ipc-{i}");
                    return p;
                }
                catch { }
            }
            return null;
        }

        private void writeFrame(int opcode, string json)
        {
            if (disposed)
                return;

            byte[] data = Encoding.UTF8.GetBytes(json);
            byte[] frame = new byte[8 + data.Length];
            writeInt32LE(frame, 0, opcode);
            writeInt32LE(frame, 4, data.Length);
            Buffer.BlockCopy(data, 0, frame, 8, data.Length);

            try
            {
                lock (writeLock)
                {
                    if (pipe == null || !pipe.IsConnected)
                        return;
                    pipe.Write(frame, 0, frame.Length);
                    pipe.Flush();
                }
                logger.Msg($"[IPC] Sent frame of length {frame.Length}");
                beginReadHeader();
            }
            catch (Exception ex)
            {
                logger.Warning($"[IPC] Send failed: {ex.Message}");
                closePipe();
            }
        }

        private byte[] readBuffer = new byte[8];

        private void beginReadHeader()
        {
            if (disposed || pipe == null || !pipe.IsConnected)
                return;
            try
            {
                readBuffer = new byte[8];
                pipe.BeginRead(readBuffer, 0, 8, onHeaderRead, null);
            }
            catch
            {
                closePipe();
            }
        }

        private void onHeaderRead(IAsyncResult ar)
        {
            if (disposed || pipe == null)
                return;
            try
            {
                int bytesRead = pipe.EndRead(ar);
                if (bytesRead != 8)
                {
                    closePipe();
                    return;
                }

                int opcode = readInt32LE(readBuffer, 0);
                int length = readInt32LE(readBuffer, 4);

                if (length < 0 || length > maxbuffersize)
                {
                    closePipe();
                    return;
                }

                if (length == 0)
                {
                    handleFrame(opcode, string.Empty);
                    beginReadHeader();
                    return;
                }

                readBuffer = new byte[length];
                pipe.BeginRead(readBuffer, 0, length, ar2 => onBodyRead(ar2, opcode), null);
            }
            catch
            {
                closePipe();
            }
        }

        private void onBodyRead(IAsyncResult ar, int opcode)
        {
            if (disposed || pipe == null)
                return;
            try
            {
                int bytesRead = pipe.EndRead(ar);
                if (bytesRead <= 0)
                {
                    closePipe();
                    return;
                }

                string jsonStr = Encoding.UTF8.GetString(readBuffer, 0, bytesRead);
                logger.Msg($"[IPC] RCV (op={opcode}): {jsonStr}");
                handleFrame(opcode, jsonStr);
            }
            catch
            {
                closePipe();
            }
        }

        private void handleFrame(int opcode, string json)
        {
            logger.Msg($"[IPC] handleFrame: {json}");
            if (opcode == opclose)
            {
                logger.Warning($"[IPC] Discord closed: {json}");
                closePipe();
                return;
            }
            if (opcode != opframe)
                return;

            string? evt = extractString(json, "evt");

            if (evt == "READY")
            {
                string? username = extractString(json, "username");
                connected = true;
                logger.Msg($"[IPC] Discord Ready — {username}");
                OnReady?.Invoke();
            }
            else if (evt == "ERROR")
            {
                string? message = extractString(json, "message");
                string? code = extractString(json, "code");
                logger.Error($"[IPC] Discord error {code}: {message}");
                OnError?.Invoke($"{code}: {message}");
            }
        }

        private void closePipe()
        {
            connected = false;
            try
            {
                pipe?.Close();
            }
            catch { }
            try
            {
                pipe?.Dispose();
            }
            catch { }
            pipe = null;
            reconnectSignal.Set();
        }

        private string buildActivity(
            string? details,
            string? state,
            string? largeImageKey,
            string? largeImageText,
            string? smallImageKey,
            string? smallImageText,
            string? buttonLabel,
            string? buttonUrl
        )
        {
            var sb = new StringBuilder("{");
            if (!string.IsNullOrEmpty(details))
                appendKv(sb, "details", details!);
            if (!string.IsNullOrEmpty(state))
                appendKv(sb, "state", state!);

            if (sb.Length > 1)
                sb.Append(',');
            sb.Append($"\"timestamps\":{{\"start\":{startTimeUnix}}}");

            if (!string.IsNullOrEmpty(largeImageKey) || !string.IsNullOrEmpty(smallImageKey))
            {
                sb.Append(',');
                sb.Append("\"assets\":{");
                if (!string.IsNullOrEmpty(largeImageKey))
                {
                    sb.Append($"\"large_image\":{jsonString(largeImageKey!)}");
                    if (!string.IsNullOrEmpty(largeImageText))
                        sb.Append($",\"large_text\":{jsonString(largeImageText!)}");
                }
                if (!string.IsNullOrEmpty(smallImageKey))
                {
                    if (!string.IsNullOrEmpty(largeImageKey))
                        sb.Append(',');
                    sb.Append($"\"small_image\":{jsonString(smallImageKey!)}");
                    if (!string.IsNullOrEmpty(smallImageText))
                        sb.Append($",\"small_text\":{jsonString(smallImageText!)}");
                }
                sb.Append('}');
            }

            if (!string.IsNullOrEmpty(buttonLabel) && !string.IsNullOrEmpty(buttonUrl))
            {
                sb.Append(",\"buttons\":[{");
                sb.Append($"\"label\":{jsonString(buttonLabel!)},");
                sb.Append($"\"url\":{jsonString(buttonUrl!)}");
                sb.Append("}]");
            }

            sb.Append('}');
            return sb.ToString();
        }

        private void appendKv(StringBuilder sb, string key, string value)
        {
            if (sb.Length > 1)
                sb.Append(',');
            sb.Append($"\"{key}\":{jsonString(value)}");
        }

        private static string jsonString(string s) =>
            "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static string? extractString(string json, string key)
        {
            string search = $"\"{key}\"";
            int ki = json.IndexOf(search, StringComparison.Ordinal);
            if (ki < 0)
                return null;
            int colon = json.IndexOf(':', ki + search.Length);
            if (colon < 0)
                return null;
            int start = json.IndexOf('"', colon + 1);
            if (start < 0)
                return null;
            int end = json.IndexOf('"', start + 1);
            if (end < 0)
                return null;
            return json.Substring(start + 1, end - start - 1);
        }

        private static void writeInt32LE(byte[] buf, int offset, int value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
            buf[offset + 2] = (byte)((value >> 16) & 0xFF);
            buf[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static int readInt32LE(byte[] buf, int offset) =>
            buf[offset]
            | (buf[offset + 1] << 8)
            | (buf[offset + 2] << 16)
            | (buf[offset + 3] << 24);
    }
}

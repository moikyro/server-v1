using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace server
{
    public class ChatHandler : IDisposable
    {
        private readonly NetworkStream _stream;
        private readonly Action<string> _messageReceivedCallback;
        private readonly CancellationTokenSource _cts;
        private bool _disposed;

        public ChatHandler(NetworkStream stream, Action<string> messageReceivedCallback)
        {
            _stream = stream;
            _messageReceivedCallback = messageReceivedCallback ?? throw new ArgumentNullException(nameof(messageReceivedCallback));
            _cts = new CancellationTokenSource();
        }

        public void StartReceiving()
        {
            ThreadPool.QueueUserWorkItem(state => ReceiveMessages(_cts.Token));
        }

        private void ReceiveMessages(CancellationToken cancellationToken)
        {
            try
            {
                byte[] lengthBuffer = new byte[4];

                while (!cancellationToken.IsCancellationRequested)
                {
                    // Leer longitud del mensaje
                    int bytesRead = _stream.Read(lengthBuffer, 0, 4);
                    if (bytesRead == 0) break; 

                    int messageLength = BitConverter.ToInt32(lengthBuffer, 0);

                    // Leer el mensaje completo
                    byte[] messageBuffer = new byte[messageLength];
                    int totalRead = 0;

                    while (totalRead < messageLength)
                    {
                        bytesRead = _stream.Read(messageBuffer, totalRead, messageLength - totalRead);
                        if (bytesRead == 0) throw new EndOfStreamException();
                        totalRead += bytesRead;
                    }

                    string message = Encoding.UTF8.GetString(messageBuffer);
                    _messageReceivedCallback?.Invoke(message);
                }
            }
            catch (OperationCanceledException)
            {
                // Salida normal por cancelación
            }
            catch (Exception ex)
            {
                _messageReceivedCallback?.Invoke($"Error: {ex.Message}");
            }
        }

        public void SendMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                throw new ArgumentException("Message cannot be null or empty", nameof(message));

            try
            {
                byte[] messageBytes = Encoding.UTF8.GetBytes(message);
                byte[] lengthBytes = BitConverter.GetBytes(messageBytes.Length);

                _stream.Write(lengthBytes, 0, lengthBytes.Length);
                _stream.Write(messageBytes, 0, messageBytes.Length);
            }
            catch (Exception ex)
            {
                _messageReceivedCallback?.Invoke($"Send error: {ex.Message}");
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _cts.Cancel();
            _cts.Dispose();
            _stream?.Dispose();

            _disposed = true;
        }
    }
}

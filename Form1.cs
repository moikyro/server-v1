using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace server
{
    public partial class Form1 : Form, IDisposable
    {
        private string IP;
        private const int PORT = 1900;
        private TcpClient client;
        private TcpListener listener;
        private ChatHandler chatHandler;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private bool _disposed = false;

        // Controles UI
        private Form chatForm = new Form { Text = "Chat", Size = new Size(400, 300) };
        private TextBox chatBox = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, ReadOnly = true };
        private TextBox inputBox = new TextBox { Dock = DockStyle.Bottom };
        private Button sendButton = new Button { Text = "Enviar", Dock = DockStyle.Bottom };

        public Form1()
        {
            InitializeComponent();
            SetupChatForm();
        }

        private void SetupChatForm()
        {
            sendButton.Click += (s, e) => SendChatMessage();
            inputBox.KeyPress += (s, e) => { if (e.KeyChar == (char)Keys.Enter) SendChatMessage(); };

            var btnEnviarArchivo = new Button { Text = "Enviar archivo", Dock = DockStyle.Bottom, Size = new Size(120, 30) };
            btnEnviarArchivo.Click += ExchangeFile;

            chatForm.Controls.Add(chatBox);
            chatForm.Controls.Add(inputBox);
            chatForm.Controls.Add(sendButton);
            chatForm.Controls.Add(btnEnviarArchivo);
            chatForm.FormClosing += (s, e) => e.Cancel = true; // Previene cierre accidental
        }

        private void StartServer()
        {
            try
            {
                listener = new TcpListener(IPAddress.Parse(IP), PORT);
                listener.Start();
                AcceptConnection();
            }
            catch (Exception ex)
            {
                ShowError($"Error al iniciar servidor: {ex.Message}");
            }
        }

        private void AcceptConnection()
        {
            try
            {
                client = listener.AcceptTcpClient();
                var stream = client.GetStream();

                // Solicitar confirmación
                SendMessage(stream, "¿Aceptar conexión?");
                string respuesta = ReceiveMessage(stream);

                if (respuesta?.ToLower() == "si")
                {
                    this.Invoke((MethodInvoker)(() =>
                    {
                        MessageBox.Show("Conexión aceptada");
                        chatForm.Show();
                    }));

                    chatHandler = new ChatHandler(stream, message =>
                    {
                        this.Invoke((MethodInvoker)(() =>
                        {
                            chatBox.AppendText($"Cliente: {message}{Environment.NewLine}");
                        }));
                    });
                    chatHandler.StartReceiving();

                    StartScreenSharing();
                }
                else
                {
                    HandleRejectedConnection();
                }
            }
            catch (Exception ex)
            {
                ShowError($"Error en conexión: {ex.Message}");
            }
        }

        private void StartScreenSharing()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        using (var bitmap = new Bitmap(Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height))
                        using (var g = Graphics.FromImage(bitmap))
                        {
                            g.CopyFromScreen(0, 0, 0, 0, bitmap.Size);
                            this.Invoke((MethodInvoker)(() =>
                            {
                                this.BackgroundImage = new Bitmap(bitmap);
                            }));
                        }
                        Thread.Sleep(100);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        ShowError($"Error sharing screen: {ex.Message}");
                    }
                }
            });
        }

        private void SendChatMessage()
        {
            if (!string.IsNullOrEmpty(inputBox.Text))
            {
                chatHandler.SendMessage(inputBox.Text);
                chatBox.AppendText($"Yo: {inputBox.Text}{Environment.NewLine}");
                inputBox.Clear();
            }
        }

        private void ExchangeFile(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        string fileName = Path.GetFileName(ofd.FileName);
                        byte[] fileData = File.ReadAllBytes(ofd.FileName);

                        // Enviar primero información del archivo
                        chatHandler.SendMessage($"FILE|{fileName}|{fileData.Length}");

                        // Luego enviar los datos en chunks
                        int chunkSize = 8192;
                        for (int offset = 0; offset < fileData.Length; offset += chunkSize)
                        {
                            int remaining = Math.Min(chunkSize, fileData.Length - offset);
                            chatHandler.SendMessage(Convert.ToBase64String(fileData, offset, remaining));
                            Thread.Sleep(10); // Pequeña pausa para evitar saturación
                        }

                        MessageBox.Show("Archivo enviado correctamente");
                    }
                    catch (Exception ex)
                    {
                        ShowError($"Error al enviar archivo: {ex.Message}");
                    }
                }
            }
        }

        private void HandleRejectedConnection()
        {
            this.Invoke((MethodInvoker)(() =>
            {
                var result = MessageBox.Show("Conexión rechazada. ¿Reintentar?", "Confirmación", MessageBoxButtons.RetryCancel);
                if (result == DialogResult.Retry)
                {
                    AcceptConnection();
                }
                else
                {
                    DisposeResources();
                }
            }));
        }

        private void ShowError(string message)
        {
            this.Invoke((MethodInvoker)(() => MessageBox.Show(message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
        }

        // Métodos auxiliares para comunicación inicial
        private void SendMessage(NetworkStream stream, string message)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            stream.Write(buffer, 0, buffer.Length);
        }

        private string ReceiveMessage(NetworkStream stream)
        {
            byte[] buffer = new byte[256];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            return Encoding.UTF8.GetString(buffer, 0, bytesRead);
        }

        private void btnConnect_Click(object sender, EventArgs e)
        {
            if (!IPAddress.TryParse($"{ip1.Text}.{ip2.Text}.{ip3.Text}.{ip4.Text}", out _))
            {
                MessageBox.Show("Dirección IP no válida");
                return;
            }

            IP = $"{ip1.Text}.{ip2.Text}.{ip3.Text}.{ip4.Text}";
            btnConnect.Enabled = false;

            ThreadPool.QueueUserWorkItem(_ => StartServer());
        }

        private void DisposeResources()
        {
            _cts.Cancel();
            chatHandler?.Dispose();
            client?.Close();
            listener?.Stop();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            DisposeResources();
            base.OnFormClosing(e);
        }

        public new void Dispose()
        {
            if (_disposed) return;

            DisposeResources();
            _cts.Dispose();
            base.Dispose();

            _disposed = true;
        }
    }
}
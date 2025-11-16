using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public class ChatClientImproved
{
    private TcpClient _client = new TcpClient();
    private NetworkStream? _stream;
    private string _username = string.Empty;
    private CancellationTokenSource _cts = new CancellationTokenSource();

    // Shared lock to ensure only one thread writes to the console at a time.
    private static readonly object ConsoleLock = new object();

    public async Task Connect(string serverIP, int serverPort)
    {
        Console.Title = $"ShadeNet Client | Connecting to {serverIP}:{serverPort}";

        // Necessary for C# console to handle non-ASCII characters correctly
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Console.Write("Enter your username: ");
        _username = Console.ReadLine()?.Trim() ?? "Anonymous";

        try
        {
            await _client.ConnectAsync(serverIP, serverPort);
            _stream = _client.GetStream();

            // 1. Sends the nickname FIRST
            byte[] usernameBytes = Encoding.UTF8.GetBytes(_username);
            await _stream.WriteAsync(usernameBytes, 0, usernameBytes.Length);

            Console.WriteLine("\n--- Connected. Type 'quit' or /exit to disconnect. ---\n");

            // Starts background receiving
            Task.Run(() => ReceiveMessages(_cts.Token));

            // Starts the input loop (sending)
            await SendMessages();
        }
        catch (SocketException ex)
        {
            lock (ConsoleLock)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[ERROR] Failed to connect to server: {ex.Message}");
                Console.ResetColor();
            }
        }
        finally
        {
            Cleanup();
        }
    }

    private async Task SendMessages()
    {
        while (!_cts.IsCancellationRequested)
        {
            string? message;

            // 1. Draws the input prompt
            lock (ConsoleLock)
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.Write("You: ");
                Console.ResetColor();
            }

            // 2. Waits for user input in a blocking manner
            // The receiving thread (ReceiveMessages) is responsible for clearing this line
            // and redrawing the prompt if a message arrives.
            message = Console.ReadLine();

            if (message?.ToLower() == "quit" || message?.ToLower() == "/exit")
            {
                _cts.Cancel();
                break;
            }

            if (!string.IsNullOrWhiteSpace(message))
            {
                try
                {
                    // Sends the complete message (the server will relay it)
                    byte[] messageBytes = Encoding.UTF8.GetBytes(message);
                    await _stream!.WriteAsync(messageBytes, 0, messageBytes.Length);
                }
                catch (Exception)
                {
                    // Send error (server dropped)
                    lock (ConsoleLock)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("\n[SEND ERROR] Connection lost.");
                        Console.ResetColor();
                    }
                    _cts.Cancel();
                }
            }
        }
    }

    private async Task ReceiveMessages(CancellationToken token)
    {
        byte[] buffer = new byte[4096];

        try
        {
            while (!token.IsCancellationRequested)
            {
                int bytesRead = await _stream!.ReadAsync(buffer, 0, buffer.Length, token);

                if (bytesRead == 0) // Server closed connection
                {
                    throw new SocketException();
                }

                string fullResponse = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                // Ensures console writing is atomic to avoid input corruption
                lock (ConsoleLock)
                {
                    // 1. Saves the current cursor position (where ReadLine is blocked)
                    int currentLineCursor = Console.CursorTop;

                    // 2. Moves the cursor to the start of the line
                    Console.SetCursorPosition(0, currentLineCursor);

                    // 3. Clears the entire line (removes "You: " and whatever the user typed)
                    Console.Write(new string(' ', Console.WindowWidth));

                    // 4. Returns to the start of the cleared line (maintaining the same line)
                    Console.SetCursorPosition(0, currentLineCursor);

                    // --- 5. Formatting and Writing the Received Message ---
                    FormatAndWriteMessage(fullResponse);

                    // 6. The WriteLine in the function above pushed the cursor to the next line.
                    // Now, we redraw the prompt on the new clear line.
                    Console.ForegroundColor = ConsoleColor.Blue;
                    Console.Write("You: ");
                    Console.ResetColor();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Operation was cancelled (user typed 'quit')
        }
        catch (Exception)
        {
            // Network error or server closed
            if (!_cts.IsCancellationRequested)
            {
                lock (ConsoleLock)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n[CONNECTION ERROR] Server disconnected. Press Enter to exit.");
                    Console.ResetColor();
                }
                _cts.Cancel();
            }
        }
    }

    // New method to handle formatting and color coding
    private void FormatAndWriteMessage(string message)
    {
        // System and command messages (like /list, /help)
        if (message.StartsWith("[SERVER]") || message.StartsWith("[SERVER ANNOUNCEMENT]"))
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine(message);
        }
        // Private messages
        else if (message.StartsWith("[WHISPER"))
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine(message);
        }
        // Regular chat messages (public)
        else if (message.StartsWith("["))
        {
            // Tries to break down the message: [Name]: Message
            int nameEnd = message.IndexOf(']');
            if (nameEnd > 0 && message.Substring(nameEnd).TrimStart().StartsWith(":"))
            {
                // Nickname
                string nicknamePart = message.Substring(0, nameEnd + 1);
                // Message (skips the ": ")
                string messagePart = message.Substring(nameEnd + 1).TrimStart(' ', ':');

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(nicknamePart); // Ex: [OtherUser]

                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($": {messagePart}"); // Ex: : Hello!
            }
            else
            {
                // Fallback for unexpected format
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(message);
            }
        }
        else
        {
            // System messages (user connection/disconnection, etc.)
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine(message);
        }

        Console.ResetColor();
    }

    private void Cleanup()
    {
        if (_stream != null)
        {
            _stream.Close();
        }
        if (_client.Connected)
        {
            _client.Close();
        }
        lock (ConsoleLock)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n--- Disconnected from chat. Press Enter to close. ---\n");
            Console.ResetColor();
        }
    }
}

public class ProgramImproved
{
    public static async Task Main(string[] args)
    {
        Console.Clear();

        // App Style and Title (ShadeNet branding)
        Console.ForegroundColor = ConsoleColor.DarkMagenta;
        Console.WriteLine("---------------------------------------------");
        Console.WriteLine("|              ShadeNet Cipher              |");
        Console.WriteLine("|       Covert Communication Terminal       |");
        Console.WriteLine("---------------------------------------------");
        Console.ResetColor();

        // Requests IP and Port
        Console.Write("Enter server IP address (e.g., 127.0.0.1): ");
        string serverIP = Console.ReadLine() ?? "127.0.0.1";

        Console.Write("Enter server port (e.g., 5000): ");
        if (!int.TryParse(Console.ReadLine(), out int serverPort))
        {
            serverPort = 5000;
        }

        ChatClientImproved client = new ChatClientImproved();
        await client.Connect(serverIP, serverPort);

        // Waits for final Enter to prevent immediate console closing
        Console.ReadLine();
    }
}
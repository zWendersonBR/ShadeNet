using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Class that manages the connection and logic for a single client
public class ClientHandler
{
    private TcpClient _client;
    private ShadeNetChatServer _server;
    private NetworkStream _stream;
    private string _username = "Unknown";
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();

    // Public property to access the username (Thread-safe)
    public string Username => _username;

    // Constructor
    public ClientHandler(TcpClient client, ShadeNetChatServer server)
    {
        _client = client;
        _server = server;
        _stream = client.GetStream();
    }

    // Main method to handle the client asynchronously
    public async Task HandleClientAsync()
    {
        try
        {
            // 1. Receive the username (First message)
            await ReceiveUsernameAsync();

            // 2. Notify entry and start the receiving loop
            _server.BroadcastSystemMessage($"{_username} ({((IPEndPoint)_client.Client.RemoteEndPoint!).Address}) has joined the chat!");

            // 3. Main loop to receive and process messages
            await ReceiveMessagesLoopAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Cancellation due to /kick or server shutdown
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Client {_username} ({((IPEndPoint)_client.Client.RemoteEndPoint!).Address}): {ex.Message}");
        }
        finally
        {
            // 4. Disconnection and cleanup
            Disconnect();
            _server.RemoveClient(this);
            _server.BroadcastSystemMessage($"{_username} has left the chat.");
        }
    }

    private async Task ReceiveUsernameAsync()
    {
        byte[] buffer = new byte[1024];
        int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
        _username = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

        // Send private welcome message
        await SendMessageAsync($"[SERVER] Welcome, {_username}! Type /help for commands.");
        Console.WriteLine($"\n[CONNECTED] New client: {_username} | IP: {((IPEndPoint)_client.Client.RemoteEndPoint!).Address}");
    }

    private async Task ReceiveMessagesLoopAsync(CancellationToken token)
    {
        byte[] buffer = new byte[4096];

        while (!token.IsCancellationRequested)
        {
            int bytesRead;
            try
            {
                bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, token);
            }
            catch (Exception)
            {
                // Client closed the connection abruptly
                break;
            }

            if (bytesRead == 0) // Client disconnected
            {
                break;
            }

            string rawMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

            // Check if it is a user command
            if (rawMessage.StartsWith("/"))
            {
                await HandleCommandAsync(rawMessage);
            }
            else
            {
                // Regular message: Format and send to everyone
                _server.BroadcastMessage($"[{_username}]: {rawMessage}", this);
                Console.WriteLine($"[CHAT] {_username}: {rawMessage}");
            }
        }
    }

    private async Task HandleCommandAsync(string command)
    {
        string[] parts = command.Split(' ', 2);
        string cmd = parts[0].ToLower();
        string args = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        switch (cmd)
        {
            case "/list":
                string userList = "Online Users: " + string.Join(", ", _server.GetClientUsernames());
                await SendMessageAsync($"[SERVER] {userList}");
                break;

            case "/whisper":
                await HandleWhisperCommandAsync(args);
                break;

            case "/help":
                string helpMessage = "Available Commands: /list (View users), /whisper <user> <message> (Private message), /exit (Quit).";
                await SendMessageAsync($"[SERVER] {helpMessage}");
                break;

            case "/exit":
                await SendMessageAsync("[SERVER] You requested disconnection. Goodbye!");
                _cts.Cancel();
                break;

            default:
                await SendMessageAsync($"[SERVER] Unknown command: {cmd}. Type /help.");
                break;
        }
    }

    private async Task HandleWhisperCommandAsync(string args)
    {
        if (string.IsNullOrWhiteSpace(args))
        {
            await SendMessageAsync("[SERVER] Usage: /whisper <user> <message>");
            return;
        }

        string[] whisperParts = args.Split(' ', 2);
        if (whisperParts.Length < 2)
        {
            await SendMessageAsync("[SERVER] Incomplete private message. Usage: /whisper <user> <message>");
            return;
        }

        string targetUsername = whisperParts[0];
        string message = whisperParts[1];

        if (targetUsername.Equals(_username, StringComparison.OrdinalIgnoreCase))
        {
            await SendMessageAsync("[SERVER] No need to whisper to yourself.");
            return;
        }

        bool success = _server.SendWhisper(_username, targetUsername, message);

        if (success)
        {
            await SendMessageAsync($"[WHISPER to {targetUsername}]: {message}");
        }
        else
        {
            await SendMessageAsync($"[SERVER] User '{targetUsername}' not found.");
        }
    }

    // Sends a message directly to this client (asynchronous)
    public async Task SendMessageAsync(string message)
    {
        try
        {
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            await _stream.WriteAsync(messageBytes, 0, messageBytes.Length);
            await _stream.FlushAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to send to {_username}: {ex.Message}");
            Disconnect();
        }
    }

    public void Disconnect()
    {
        try
        {
            _cts.Cancel();
            _stream.Close();
            _client.Close();
        }
        catch { /* Ignore errors on close */ }
    }
}

// Class that manages listening for connections and the list of clients
public class ShadeNetChatServer
{
    private TcpListener? _listener;
    // We use List and 'lock' for thread-safety when modifying the list
    private readonly List<ClientHandler> _clients = new List<ClientHandler>();
    private readonly object _lock = new object();
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();

    public ShadeNetChatServer(string ipAddress, int port)
    {
        try
        {
            IPAddress address = IPAddress.Parse(ipAddress);
            _listener = new TcpListener(address, port);
            Console.Title = $"ShadeNet Chat Server | {ipAddress}:{port}";
        }
        catch (FormatException)
        {
            Console.WriteLine("[ERROR] Invalid IP address format.");
            throw;
        }
    }

    public async Task StartAsync()
    {
        if (_listener == null) return;

        _listener.Start();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Chat server started. Waiting for clients...");
        Console.ResetColor();

        // Task to accept new connections
        var acceptTask = AcceptClientsAsync(_cts.Token);

        // Task for server console commands
        var serverCommandTask = HandleServerCommandsAsync();

        // Wait for both tasks to finish (accepting clients and commands)
        await Task.WhenAny(acceptTask, serverCommandTask);

        // If the server is shut down via the console, cancel the accept task
        if (serverCommandTask.IsCompleted)
        {
            _cts.Cancel();
        }

        // Final cleanup
        Shutdown();
    }

    private async Task AcceptClientsAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener != null)
        {
            try
            {
                // Accept the connection asynchronously
                TcpClient client = await _listener.AcceptTcpClientAsync(token);

                // Create the handler and add it to the list
                ClientHandler clientHandler = new ClientHandler(client, this);
                AddClient(clientHandler);

                // Start processing the client in a new Task
                Task.Run(() => clientHandler.HandleClientAsync(), token);
            }
            catch (OperationCanceledException)
            {
                // Listener was stopped (shutdown)
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CONNECTION ERROR] {ex.Message}");
            }
        }
    }

    private async Task HandleServerCommandsAsync()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Server Console: Type /list, /sysmsg <msg> or /exit to view commands.");
        Console.ResetColor();

        while (!_cts.IsCancellationRequested)
        {
            string? input = await Task.Run(() => Console.ReadLine());

            if (string.IsNullOrWhiteSpace(input)) continue;

            string[] parts = input.Trim().Split(' ', 2);
            string command = parts[0].ToLower();
            string args = parts.Length > 1 ? parts[1].Trim() : string.Empty;

            switch (command)
            {
                case "/list":
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("--- CONNECTED CLIENTS ---");
                    var usernames = GetClientUsernames();
                    if (usernames.Any())
                    {
                        foreach (var name in usernames)
                        {
                            Console.WriteLine($"- {name}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("No clients connected.");
                    }
                    Console.ResetColor();
                    break;

                case "/sysmsg":
                    if (!string.IsNullOrWhiteSpace(args))
                    {
                        BroadcastSystemMessage($"[SERVER ANNOUNCEMENT] {args}");
                        Console.ForegroundColor = ConsoleColor.DarkGreen;
                        Console.WriteLine($"System message sent: {args}");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine("Usage: /sysmsg <message>");
                    }
                    break;

                case "/exit":
                case "/shutdown":
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Initiating server shutdown...");
                    Console.ResetColor();
                    _cts.Cancel();
                    return;

                default:
                    Console.WriteLine($"Unknown console command: {command}");
                    break;
            }
        }
    }

    private void AddClient(ClientHandler client)
    {
        lock (_lock)
        {
            _clients.Add(client);
        }
    }

    // Removes the client (used by the ClientHandler when disconnecting)
    public void RemoveClient(ClientHandler client)
    {
        lock (_lock)
        {
            _clients.Remove(client);
        }
    }

    // Sends the message to everyone except the sender
    public void BroadcastMessage(string formattedMessage, ClientHandler? sender)
    {
        lock (_lock)
        {
            foreach (var client in _clients.Where(c => c != sender))
            {
                // Send asynchronously, but do not wait for completion
                Task.Run(() => client.SendMessageAsync(formattedMessage));
            }
        }
    }

    // Sends a system message to everyone (no sender excluded)
    public void BroadcastSystemMessage(string message)
    {
        string formattedMessage = $"[SERVER] {message}";
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"[SYSTEM]: {message}");
        Console.ResetColor();

        lock (_lock)
        {
            foreach (var client in _clients)
            {
                Task.Run(() => client.SendMessageAsync(formattedMessage));
            }
        }
    }

    // Sends a private message to a specific user
    public bool SendWhisper(string senderUsername, string targetUsername, string message)
    {
        ClientHandler? targetClient;
        lock (_lock)
        {
            targetClient = _clients.FirstOrDefault(c => c.Username.Equals(targetUsername, StringComparison.OrdinalIgnoreCase));
        }

        if (targetClient != null)
        {
            // Message the recipient sees
            Task.Run(() => targetClient.SendMessageAsync($"[WHISPER from {senderUsername}]: {message}"));
            Console.WriteLine($"[WHISPER] {senderUsername} -> {targetUsername}: {message}");
            return true;
        }
        return false;
    }

    // Returns the list of connected usernames
    public IEnumerable<string> GetClientUsernames()
    {
        lock (_lock)
        {
            // Use .ToList() to prevent modification during iteration
            return _clients.Select(c => c.Username).ToList();
        }
    }

    // Shuts down the listener and disconnects all clients
    public void Shutdown()
    {
        _cts.Cancel(); // Cancel the accept loop
        _listener?.Stop();

        // Disconnect all clients
        lock (_lock)
        {
            foreach (var client in _clients.ToList()) // ToList to copy the list before modifying it
            {
                client.Disconnect();
            }
            _clients.Clear();
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("\nServer Shut Down.");
        Console.ResetColor();
    }
}

public class ServerProgram
{
    public static async Task Main(string[] args)
    {
        // Set the default encoding method to UTF8
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Console.Clear();

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("---------------------------------------------");
        Console.WriteLine("|         ShadeNet Server Cipher            |");
        Console.WriteLine("|       Sever Communication Terminal        |");
        Console.WriteLine("---------------------------------------------");
        Console.ResetColor();

        // Get IP and Port (Uses default loopback if empty)
        Console.Write("Enter server IP address (e.g., 127.0.0.1, Empty for 127.0.0.1): ");
        string ipAddress = Console.ReadLine() ?? "127.0.0.1";
        if (string.IsNullOrWhiteSpace(ipAddress)) ipAddress = "127.0.0.1";

        Console.Write("Enter server port (e.g., 5000, Empty for 5000): ");
        if (!int.TryParse(Console.ReadLine(), out int port))
        {
            port = 5000;
        }

        try
        {
            ShadeNetChatServer server = new ShadeNetChatServer(ipAddress, port);
            await server.StartAsync();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.DarkRed;
            Console.WriteLine($"\nFATAL ERROR: {ex.Message}");
            Console.WriteLine("Please check the IP address and port.");
            Console.ResetColor();
        }
    }
}
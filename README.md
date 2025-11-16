# ShadeNet Cipher  
**Secure TCP/IP Communication (C# / .NET)**
---
>[WARNING]
>Privacy Notice & Responsible Use
>ShadeNet Cipher is designed to operate without persistent storage:
>• No chat logs are saved.
>• No message history is retained after the server shuts down.
>• The server actively monitors for abnormal or unauthorized interception attempts and will display warnings if such activity is detected.
>This behavior aims to provide ephemeral, privacy-focused communication, suitable for temporary and disposable chat sessions.
>However, privacy does not remove responsibility.
>Users must follow all applicable laws and use the software ethically. ShadeNet Cipher is not intended for illegal activity, evasion of law enforcement, or harmful behavior of any kind.
>By using this tool, you agree that all responsibility for its usage lies solely with you (the user). The creator(s) are not liable for any misuse.
>Use ShadeNet Cipher responsibly, ethically, and within legal boundaries.

---


ShadeNet Cipher is a command-line application built in **C# (.NET 8)** for studying and demonstrating asynchronous TCP/IP sockets.  
The project includes:

- **ShadeNetServer** — handles multiple simultaneous client connections  
- **ShadeNetClient** — connects to the server and provides a console-based chat  

Perfect for networking study and experimentation.

---

## 🚀 Prerequisites

You must have **.NET SDK 8.0** installed to build and run this project.

---

# 💻 Installation Guide for Linux (Debian/Ubuntu)

Run the following commands in your terminal:

### 1. Install basic dependencies
```bash
sudo apt update
sudo apt install -y curl
````

### 2. Install certificates and GnuPG

```bash
sudo apt install -y ca-certificates gnupg
```

### 3. Download and install Microsoft’s official package signing key

```bash
sudo gpg --dearmor -o /etc/apt/keyrings/microsoft.gpg
curl -fsSL https://packages.microsoft.com/keys/microsoft.asc \
    | sudo tee /etc/apt/keyrings/microsoft.asc > /dev/null
```

### 4. Add the Microsoft package repository

```bash
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/microsoft.gpg] \
https://packages.microsoft.com/ubuntu/$(lsb_release -rs)/prod \
$(lsb_release -cs) main" \
| sudo tee /etc/apt/sources.list.d/microsoft-prod.list
```

### 5. Install the .NET 8 SDK

```bash
sudo apt update
sudo apt install -y dotnet-sdk-8.0
```

### 6. Verify installation

```bash
dotnet --version
```

---

# 🪟 Installation Guide for Windows

Download and run the official .NET SDK 8.0 installer:
🔗 [https://dotnet.microsoft.com/en-us/download/dotnet/8.0](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

---

# 🚀 Running the Project Locally

You will need **two terminal windows**—one for the server and one for the client.

---

## 1. Start the Server (ShadeNetServer)

```bash
cd ShadeNetCipher/ShadeNetServer
dotnet run
```

The server will ask for:

* Listening IP
* Port

For local testing, use:

* **IP:** `127.0.0.1` or `0.0.0.0`
* **Port:** `5000` (or any other)

---

## 2. Start the Client (ShadeNetClient)

```bash
cd ShadeNetCipher/ShadeNetClient
dotnet run
```

The client will ask for:

* Server IP
* Port
* Nickname

For local use, enter the same IP and port used in the server.

---

# 🌐 Remote Access / Internet Hosting

To allow others to connect to your ShadeNet server over the internet, you must configure your network properly.

---

## 🔑 A. Connection Parameters

* **Server Listening IP:** Always use `0.0.0.0` to bind to all interfaces.
* **Client Connection IP:** Clients must use your **Public IP Address**.
* **Port:** Must match the port configured in the server.

---

## 🛡️ B. Network Configuration (Firewall & Port Forwarding)

### 1. Allow the port in your OS firewall

**Windows:**
Create an **Inbound Rule** for the port (TCP).

**Linux (UFW example):**

```bash
sudo ufw allow 5000/tcp
```

### 2. Configure Port Forwarding on your router

Inside your router’s administration panel:

* External Port: `5000`
* Internal Port: `5000`
* Protocol: **TCP**
* Destination IP: Local IP of the machine running the server

---

# ⚠️ CGNAT Warning

If your ISP uses **Carrier-Grade NAT**, you **cannot** port-forward or host a public server from home.

Common CGNAT signs:

* Router WAN IP differs from your Public IP
* You share your public IP with other users

### Solutions:

* Request a **dedicated public IP** from your ISP
* Use a **VPS** or cloud provider with a real public IP
  (DigitalOcean, AWS, Oracle Cloud, etc.)

---

# 📜 License

Add your preferred license here (MIT, Apache 2.0, GPL, etc.).

---

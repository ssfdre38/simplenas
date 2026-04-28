# SimpleNAS

**Modern, lightweight NAS management panel built with C# and ASP.NET Core**

SimpleNAS is an open-source Network Attached Storage (NAS) management solution designed for simplicity and performance. Built with C# ASP.NET Core, it provides a clean web interface for managing ZFS storage pools, monitoring system resources, and configuring network shares.

🌐 **[simplenas.dev](https://simplenas.dev)**

## ✨ Features

- 🗄️ **ZFS Pool Management** - Create, monitor, and manage ZFS RAIDZ pools
- 📊 **Real-time Monitoring** - CPU, memory, disk usage, and network stats
- 🔐 **Built-in Authentication** - Secure admin panel with user management
- 🌐 **Web-based UI** - Modern, responsive dashboard accessible from any device
- 🚀 **Lightweight** - Minimal resource footprint, runs on Linux systems
- 🐧 **Linux Native** - Optimized for Ubuntu/Debian with ZFS support

## 🚀 Quick Start

### Prerequisites

- .NET 8.0 Runtime or later
- Linux system (Ubuntu 22.04+ recommended)
- ZFS utilities installed (`zfsutils-linux`)

### Installation

1. **Clone the repository**
   ```bash
   git clone https://github.com/ssfdre38/simplenas.git
   cd simplenas
   ```

2. **Build the application**
   ```bash
   cd SimpleNAS
   dotnet publish -c Release -o publish
   ```

3. **Run SimpleNAS**
   ```bash
   cd publish
   ./SimpleNAS
   ```

4. **Access the dashboard**
   
   Open your browser to `http://localhost:8000`
   
   Default credentials:
   - Username: `admin`
   - Password: `SimpleNAS2026`
   
   ⚠️ **Change the default password immediately after first login!**

## 🔧 Configuration

Edit `appsettings.json` to customize:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "AllowedHosts": "*"
}
```

### Running as a Service

Create a systemd service at `/etc/systemd/system/simplenas.service`:

```ini
[Unit]
Description=SimpleNAS Management Panel
After=network.target

[Service]
Type=notify
WorkingDirectory=/opt/simplenas
ExecStart=/opt/simplenas/SimpleNAS
Restart=always
User=root

[Install]
WantedBy=multi-user.target
```

Enable and start:
```bash
sudo systemctl enable simplenas
sudo systemctl start simplenas
```

## 🛠️ Development

### Building from Source

```bash
dotnet restore
dotnet build
dotnet run
```

### Tech Stack

- **Backend**: ASP.NET Core 8.0 (C#)
- **Frontend**: HTML, CSS, JavaScript (vanilla)
- **Storage**: ZFS integration via shell commands
- **Authentication**: Cookie-based sessions

## 📋 Roadmap

- [ ] Multi-user support with role-based access
- [ ] SMB/NFS share management UI
- [ ] Automated backups and snapshots
- [ ] Email/webhook notifications
- [ ] Docker support
- [ ] HTTPS/SSL configuration helper
- [ ] Plugin system for extensibility

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## 📝 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- Built with [ASP.NET Core](https://dotnet.microsoft.com/apps/aspnet)
- ZFS on Linux by [OpenZFS](https://openzfs.org/)
- Inspired by the need for simple, self-hosted NAS solutions

## 💬 Support

- 🐛 [Report bugs](https://github.com/ssfdre38/simplenas/issues)
- 💡 [Request features](https://github.com/ssfdre38/simplenas/issues)
- 📧 Contact: [GitHub @ssfdre38](https://github.com/ssfdre38)

---

**Made with ❤️ for the self-hosted community**

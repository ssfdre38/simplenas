# SimpleNAS

[![Sponsored by Barrer Software](https://img.shields.io/badge/Sponsored_by-Barrer_Software-0A0E27?style=for-the-badge)](https://barrersoftware.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=for-the-badge)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/)

**Modern, lightweight NAS management panel built with C# 14 and ASP.NET Core (.NET 10)**

SimpleNAS is an open-source Network Attached Storage (NAS) management solution designed for simplicity and performance. Built with C# ASP.NET Core, it provides a clean web interface for managing ZFS storage pools, monitoring system resources, and configuring network shares.

🌐 **[simplenas.dev](https://simplenas.dev)**

> **Enterprise Support:** Backed by [Barrer Software](https://barrersoftware.com) — Professional support, consulting, and managed hosting available.

## ✨ Features

- 🗄️ **ZFS Pool Management** - Create, monitor, and manage ZFS RAIDZ pools
- 📊 **Real-time Monitoring** - CPU, memory, disk usage, and network stats
- 🔐 **Built-in Authentication** - Secure admin panel with user management
- 🌐 **Web-based UI** - Modern, responsive dashboard accessible from any device
- 🚀 **Lightweight** - Minimal resource footprint, runs on Linux systems
- 🐧 **Linux Native** - Optimized for Ubuntu/Debian with ZFS support
- ☁️ **Cloud Storage & Hybrid Pools** - Mount Google Drive and combine it with local storage using Rclone + MergerFS

## 🚀 Quick Start

### Prerequisites

- .NET 10.0 Runtime or later
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

- **Backend**: ASP.NET Core (.NET 10.0 / C# 14)
- **Frontend**: HTML, CSS, JavaScript (vanilla)
- **Storage**: ZFS integration via shell commands
- **Authentication**: Cookie-based sessions

## 📋 Roadmap

- [x] Multi-user support with role-based access (Admin, Operator, Viewer)
- [x] SMB/NFS share management UI (with Windows & Linux cross-platform shares)
- [x] Cloud Storage (Rclone & MergerFS) Integration
- [x] Automated backups and snapshots with scheduled retention pruning
- [x] Email/webhook notifications
- [x] Docker support (.NET 10 multi-stage build & docker-compose)
- [x] HTTPS/SSL configuration helper (Let's Encrypt ACME v2 & Self-Signed SAN)
- [x] Plugin system for extensibility (1-click marketplace catalog, JSON manifests, and runtime lifecycle)

## 🧩 Plugin & Extension Center

SimpleNAS features an extensible architecture allowing services to be installed, monitored, and launched directly from the web panel:

- **🐳 Docker Engine**: Container virtualization runtime for running microservices alongside SimpleNAS.
- **🎬 Jellyfin Media Server**: Free & open-source media streaming platform (Port `8096`).
- **🍿 Plex Media Server**: Stream media collections across devices with hardware transcoding (Port `32400`).
- **🔒 Tailscale Mesh VPN**: Zero-config mesh networking for secure remote access from anywhere.
- **⚡ Transmission**: Fast, lightweight BitTorrent background downloader with web UI (Port `9091`).
- **☁️ Nextcloud Hub**: Private cloud file storage, synchronization, and collaboration (Port `8080`).
- **🛡️ WireGuard Server**: High-performance, modern cryptographic VPN tunnel manager.
- **📁 FileBrowser UI**: Rich browser-based file manager for exploring NAS datasets and pools (Port `8082`).

### Creating Custom Plugins
Plugins can be added by placing an isolated folder in `/plugins` containing a `plugin.json` manifest:
```json
{
  "id": "custom-service",
  "name": "Custom Service",
  "description": "My custom NAS service extension",
  "version": "1.0.0",
  "author": "Community",
  "category": "Tools",
  "icon": "🔧",
  "defaultPort": 9999,
  "webPath": "/"
}
```

## 🐳 Docker Deployment

SimpleNAS can be deployed in containers using the provided multi-stage `Dockerfile` and `docker-compose.yml`:

```bash
# Build and run with Docker Compose
docker compose up -d
```

The container exposes:
- **Port 8000**: HTTP web dashboard & ACME HTTP-01 challenge routing
- **Port 8443**: HTTPS secured dashboard
- **Port 445**: SMB / Samba file sharing

## 🔒 SSL / TLS & Let's Encrypt

SimpleNAS includes automated certificate management:
- **Let's Encrypt (ACME v2)**: Request trusted certificates directly from the dashboard using HTTP-01 challenges.
- **Self-Signed Certificates**: Generate instant 2048-bit RSA SAN certificates for internal LANs or staging.
- **Dynamic Kestrel Binding**: Secures the panel on HTTPS port `8443` without requiring an external reverse proxy.

## 👥 Role-Based Access Control (RBAC)

Manage access permissions with built-in roles:
- **Admin**: Full system access (pool creation, share deletion, user management, SSL configuration).
- **Operator**: Daily operations (trigger snapshots, manage shares, restart services; cannot alter system users).
- **Viewer**: Read-only monitoring (metrics, pools, logs; all write mutations return `403 Forbidden`).


## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## 📝 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🏢 Enterprise Support

SimpleNAS is sponsored by **[Barrer Software](https://barrersoftware.com)** — a security and infrastructure software company.

**Available Services:**
- 🛠️ Professional support contracts
- 📊 Custom feature development
- 🏗️ Deployment consulting
- ☁️ Managed hosting solutions
- 🔒 Security hardening and compliance

📧 Contact: [admin@barrersoftware.com](mailto:admin@barrersoftware.com)

## 🙏 Acknowledgments

- Built with [ASP.NET Core](https://dotnet.microsoft.com/apps/aspnet)
- ZFS on Linux by [OpenZFS](https://openzfs.org/)
- Sponsored by [Barrer Software](https://barrersoftware.com)
- Inspired by the need for simple, self-hosted NAS solutions

## 💬 Support

- 🐛 [Report bugs](https://github.com/ssfdre38/simplenas/issues)
- 💡 [Request features](https://github.com/ssfdre38/simplenas/issues)
- 📧 Contact: [GitHub @ssfdre38](https://github.com/ssfdre38)
- 🏢 Enterprise: [admin@barrersoftware.com](mailto:admin@barrersoftware.com)

---

**Made with ❤️ for the self-hosted community**

**Sponsored by [Barrer Software](https://barrersoftware.com) 🛡️**

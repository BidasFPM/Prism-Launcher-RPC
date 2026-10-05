# Prism RPC

> Discord Rich Presence for **Prism Launcher** on Windows — written in C#.

Show exactly what you're playing in Prism Launcher on your Discord profile, in real time.
---

## ✨ Features

- 🎮 **Instance name** — with optional manual overrides and sort-prefix stripping
- 🧱 **Minecraft version** — read straight from `mmc-pack.json`
- 🧵 **Mod loader** — Fabric, Quilt, Forge, NeoForge, LiteLoader or Vanilla
- 📦 **Mod count** — number of `.jar` files in the `mods/` folder
- 🌐 **Server detection** — Multiplayer (with address), Singleplayer, or Main Menu
- 🔒 **Privacy first** — hides raw IPs and localhost by default
- 🧠 **RAM usage** — live from the game process
- ☕ **Java version** — read from the JRE's `release` file
- ⏱️ **Session timer** — starts when the game launches
- 📊 **Total playtime** — combined across sessions, from `instance.cfg`
- 🖥️ **Launcher idle state** — "Browsing instances · N instances"
- 🎛️ **System tray icon** — pause/resume, open config, open log, quit
- 🔄 **Hot config reload** — edit `config.json` while running, no restart needed

---

## 📋 Requirements

- **Windows 10/11**
- **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** (only to build)
- **[Discord](https://discord.com/download)** desktop app running
- **[Prism Launcher](https://prismlauncher.org)** installed
- A **Discord Application ID** — [create one here](https://discord.com/developers/applications)

---

## 🚀 Getting started

### 1. Clone & build

```powershell
git clone https://github.com/yourusername/PrismRPC.git
cd PrismRPC
dotnet build -c Release
```

The executable will be at:

```
bin\Release\net8.0-windows\PrismRpc.exe
```

### 2. Create a Discord Application

1. Go to the [Discord Developer Portal](https://discord.com/developers/applications)
2. Click **New Application** and give it a name (e.g. `Prism Launcher`)
3. Copy the **Application ID** (a long number)

### 3. First run

Run `PrismRpc.exe`. It will:

1. Create a `config/` folder next to the exe
2. Generate `config/config.json` and `config/prism_rpc.log`
3. Show an error dialog asking for your `client_id`

Open `config/config.json` (via the tray menu → **Open config.json**) and set:

```json
"client_id": "1234567890123456789"
```

Run again. Done — your Discord profile now reflects what you're playing.

---

## 🖱️ Tray menu

| Item | Action |
|---|---|
| **Status** | Current activity (read-only) |
| **Presence enabled** | Pause / resume Discord Rich Presence |
| **Open config.json** | Opens the config in Notepad |
| **Open prism_rpc.log** | Opens the log in Notepad |
| **Quit** | Exit Prism RPC |

Double-click the tray icon to quickly open the config.

---

## ⚙️ Command-line options

```powershell
PrismRpc.exe                    # Standalone (tray icon, runs until quit)
PrismRpc.exe --follow-prism     # Auto-exit when Prism Launcher closes
PrismRpc.exe --no-tray          # Run without a tray icon
PrismRpc.exe --test             # Test Discord connection (60s fake presence)
PrismRpc.exe --debug            # Verbose log to console + file
```

---

## 🔧 Configuration

The full config lives in `config/config.json`. Highlights:

| Key | Description |
|---|---|
| `client_id` | Your Discord Application ID (numbers only) |
| `poll_seconds` | How often to refresh the presence (default `5`) |
| `prism_data_dir` | Override Prism's data folder (auto-detected by default) |
| `strip_sort_prefix` | Removes sort-hack prefixes like `"AMinecraft"` → `"Minecraft"` |
| `name_overrides` | Map instance names → display names: `{ "MyInst": "Cool Pack" }` |
| `show.*` | Toggle individual fields (loader, mod count, RAM, etc.) |
| `privacy.hide_instance_name` | Show only "Minecraft" instead of the instance name |
| `privacy.hide_ip_servers` | Never show raw IPs — display "In a server" instead |
| `assets.loaders` | Map loaders to Discord asset keys |

The file is **hot-reloaded** — edit and save, no restart required.

---

## 🎨 Discord assets

For the images to show on your profile, upload these to your Discord Application's
**Rich Presence → Art Assets**:

| Asset key | Suggested image |
|---|---|
| `prism` | Prism Launcher logo |
| `minecraft` | Minecraft logo |
| `fabric` | Fabric logo |
| `quilt` | Quilt logo |
| `forge` | Forge logo |
| `neoforge` | NeoForge logo |
| `vanilla` | Grass block / vanilla icon |

You can use your own keys — just update `assets.loaders` in the config.

---

## 📁 Project layout

```
PrismRpc/
├── PrismRpc.csproj
├── Program.cs              # Entry point, CLI parsing, --test mode
├── Config.cs               # Config model + JSON load/merge/migrate
├── Log.cs                  # File + console logger
├── Helpers.cs              # Duration, IP check, sort-prefix strip, etc.
├── Detection.cs            # Process scan, instance.cfg, mmc-pack.json
├── LogReader.cs            # Parses Minecraft's latest.log
├── TcpConnections.cs       # P/Invoke into iphlpapi for real MP detection
├── PresenceBuilder.cs      # Builds the RichPresence payload
├── Worker.cs               # Background thread: poll → detect → update RPC
├── TrayApp.cs              # System tray icon + context menu
└── assets/
    └── icon.ico            # App + tray icon
```

---

## 🔍 How detection works

Prism RPC doesn't hook into Prism Launcher — it observes from the outside:

1. **Scans processes** for `prismlauncher.exe` and any `java(w).exe` whose command
   line contains `org.prismlauncher.EntryPoint`
2. **Reads the game directory** from `--gameDir` in the Java command line
3. **Parses `mmc-pack.json`** for the Minecraft version and mod loader
4. **Counts `.jar` files** in `mods/` for the mod count
5. **Tails `logs/latest.log`** to detect:
   - Game loaded → `Sound engine started`
   - Connected to server → `Connecting to <host>, <port>`
   - Singleplayer world → `Starting integrated minecraft server`
   - Left server → `Stopping server` / `Disconnected from`
6. **Checks TCP connections** (`GetExtendedTcpTable`) to know if the game is
   *actually* connected — so leaving a server updates the presence immediately

---

## 🛠️ Built with

- [.NET 8](https://dotnet.microsoft.com/) — Windows Desktop
- [DiscordRichPresence](https://github.com/Lachee/discord-rpc-csharp) — Discord RPC client
- [System.Management](https://www.nuget.org/packages/System.Management) — WMI for process command lines

---

## 📜 License

MIT — do whatever you want, attribution appreciated.

---

## 🙏 Credits

Thanks to the Prism Launcher team for a great launcher.

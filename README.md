# ConnectEV Backend (.NET Core 10 Web API)

Enterprise backend for **ConnectEV** platform supporting smart charging, telemetry, VAHAN verification, and WhatsApp-style 100km EV Community Messenger with SignalR WebSockets.

---

## 🚀 Features
* **Authentication & Google Sign-In**: Firebase & Google OAuth synchronization with local MSSQL persistence (`/api/auth/google-login`).
* **WhatsApp-Style EV Community Chat**:
  * 1-on-1 direct messaging and group convoy chats.
  * 100 km spatial nearby discovery (`/api/chat/nearby?radiusKm=100`).
  * SignalR real-time bidirectional WebSocket Hub (`/hubs/chat`).
  * Typing indicator buffer (`UserTyping` events).
  * Multi-part file & media uploads (`/api/chat/upload` saving to `wwwroot/uploads/chat`).
  * Real-time Battery SOS emergency broadcasting.
* **Smart Charging Management**: Stations, connectors, tariffs, hold reservations (15-min hold window).
* **Vehicle & VAHAN Verification**: Telemetry tracking (SOC %, temperature, charging kW power, speed).
* **Swagger UI API Documentation**: Available at `http://localhost:5000/swagger`.

---

## 🛠️ Tech Stack
* **Framework**: .NET Core 10.0 (C#)
* **Real-time WebSockets**: ASP.NET Core SignalR
* **Data Access**: Dapper + Microsoft.Data.SqlClient
* **Database**: Microsoft SQL Server (`ConnectEV` database)
* **Authentication**: JWT Bearer + Firebase Auth Integration
* **API Documentation**: Swagger / OpenAPI (Swashbuckle)

---

## 🗄️ Database Setup
Run the SQL migration scripts located in the `Data/` folder using SQL Server Management Studio or `sqlcmd`:
1. `Data/CreateChatSchema.sql` - Creates `chat.Conversations`, `chat.ConversationParticipants`, `chat.Messages`, and spatial coordinates in `identity.Users`.

---

## 🏃 Running the Backend
```bash
dotnet restore
dotnet build
dotnet run --urls "http://0.0.0.0:5000"
```
Swagger UI will be accessible at: `http://localhost:5000/swagger`

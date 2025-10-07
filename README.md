# Migración a .NET 8 (Linux)

## Requisitos
- .NET SDK 8.0
- Oracle Instant Client NO requerido (Oracle.ManagedDataAccess.Core es 100% managed)
- Acceso a Oracle y recurso de archivos (schedule_files_sp7.server_path)

## Compilar y ejecutar
```bash
dotnet restore
dotnet build -c Release
dotnet run -- 01/08/2025 05/08/2025
# o para ayer
dotnet run -- -1
```

## Publicar para Linux
```bash
dotnet publish -c Release -r linux-x64 --self-contained false -o out_linux
```

## Docker
```bash
docker build -t rspectrum7:latest .
docker run --rm -v $(pwd)/logs:/app/logs rspectrum7:latest -1
```

## systemd
Copie `readspectrum7.service` a `/etc/systemd/system/` y habilite:
```bash
sudo systemctl daemon-reload
sudo systemctl enable --now readspectrum7
sudo journalctl -u readspectrum7 -f
```

## Configuración
- **appsettings.json** (o variable `RS7_ConnectionStrings__Oracle`) define la cadena de conexión.
- **NLog.config** controla salidas a consola y archivo `./logs`.

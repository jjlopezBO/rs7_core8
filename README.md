# Proceso de Sincronización de Archivos  
## FTP → Servidor Linux → Recurso Compartido Windows (Spectrum)

---

## 1. Objetivo

Implementar un proceso automatizado y controlado para:

1. Descargar archivos desde un servidor FTP  
2. Almacenarlos en un servidor Linux  
3. Copiarlos posteriormente a un recurso compartido en Windows (Spectrum)

El flujo está diseñado para:
- Descargar solo archivos nuevos o actualizados
- Excluir tipos de archivos no requeridos
- Mantener control de errores y registros (logs)
- Separar responsabilidades entre descarga FTP y copia a Windows

---

## 2. Arquitectura General

```
FTP Server (192.168.2.125)
        |
        |  lftp mirror
        v
Linux: /data/ftproot
        |
        |  mount CIFS + rsync
        v
Windows: \\192.168.2.22\Spectrum_Data
```

---

## 3. Componentes del Proceso

### 3.1 Script `ftp_sync.sh`
Responsable de sincronizar archivos desde el servidor FTP hacia Linux.

### 3.2 Archivo `ftp_sync.conf`
Archivo de configuración con parámetros FTP y rutas locales.

### 3.3 Archivo `lftp_commands.txt`
Define explícitamente los comandos ejecutados por `lftp`.

### 3.4 Script `copy_to_Spectrum.sh`
Responsable de copiar los archivos desde Linux hacia Windows (SMB/CIFS).

---

## 4. Flujo 1: Descarga desde FTP (`ftp_sync.sh`)

### 4.1 Lógica general

1. Activa modo estricto de Bash (`set -euo pipefail`)
2. Carga configuración desde `/etc/ftp/ftp_sync.conf`
3. Verifica variables obligatorias
4. Inicializa archivo de log
5. Verifica que `lftp` esté instalado
6. Ejecuta comandos FTP desde archivo externo
7. Ajusta permisos finales
8. Registra resultado del proceso

---

### 4.2 Archivo de configuración FTP (`ftp_sync.conf`)

```bash
FTP_HOST="192.168.2.125"
FTP_USER="userftp"
FTP_PASS="userftp"
REMOTE_DIR="/"
LOCAL_DIR="/data/ftproot"
INCLUDE_GLOB="*"
EXCLUDE_GLOB="*.exe"
FTP_PASSIVE="true"
```

---

### 4.3 Comandos reales ejecutados (`lftp_commands.txt`)

```bash
open -u userftp,userftp 192.168.2.125
lcd /data/ftproot
mirror / ./ --only-newer --parallel=2   --exclude-glob="*.exe"   --exclude-glob="*.com"   --exclude-glob="*.bat"
bye
```

---

### 4.4 Resultado del primer flujo

- Los archivos quedan almacenados en:
  ```
  /data/ftproot
  ```
- Se asegura el ownership:
  ```
  root:ftpaccess
  ```

---

## 5. Flujo 2: Copia hacia Windows (`copy_to_Spectrum.sh`)

### 5.1 Lógica general

1. Define rutas locales y remotas
2. Inicializa logging
3. Valida existencia de carpeta origen y archivo de credenciales
4. Monta recurso Windows vía CIFS
5. Copia archivos con `rsync`
6. Desmonta recurso compartido
7. Registra resultado

---

### 5.2 Montaje CIFS

```text
//192.168.2.22/Spectrum_Data → /mnt/Spectrum_Data
```

---

### 5.3 Copia con rsync

```bash
rsync -a -v --update   --no-owner --no-group --no-perms --no-times   /data/ftproot/ /mnt/Spectrum_Data/
```

---

### 5.4 Resultado del segundo flujo

- Archivos disponibles en:
  ```
  \\192.168.2.22\Spectrum_Data
  ```
- Recurso desmontado automáticamente

---

## 6. Manejo de Permisos

Recomendación:

```bash
chown -R root:ftpaccess /data/ftproot
chmod -R 2775 /data/ftproot
```

---

## 7. Manejo de Logs

| Script | Log |
|------|-----|
| ftp_sync.sh | /var/log/ftp_sync.log |
| copy_to_Spectrum.sh | /var/log/smb_copy_new.log |

---

## 8. Consideraciones Operativas

- Scripts diseñados para ejecutarse como `root`
- Procesos desacoplados
- Control de errores y auditoría

---

## 9. Resumen

Cadena de sincronización robusta entre FTP, Linux y Windows, diseñada para confiabilidad, control y mantenimiento.

# RSpectrum7

Sistema automatizado de carga de datos eléctricos hacia Oracle.  
Servidor: `hfdspectrum (192.168.2.122)`  
Usuario de servicio: `ftpuser`

---

## 🧠 Descripción

RSpectrum7 es una aplicación en .NET que procesa archivos de lectura de sistemas fotovoltaicos y los carga en la base de datos Oracle CNDC.  
La ejecución está automatizada mediante **systemd** y programada para:

- **Ejecución diaria (ayer):** `00:45`
- **Ejecución continua (hoy):** cada 10 minutos

---

## ⚙️ Estructura de carpetas

| Ruta | Descripción |
|------|--------------|
| `/home/ftpuser/rspectrum7/` | Carpeta principal del sistema |
| `/home/ftpuser/rspectrum7/logs/` | Archivos de log (NLog) |
| `/home/ftpuser/rspectrum7/appsettings.json` | Configuración principal |
| `/run/rspectrum7/lock` | Archivo de bloqueo interno |

---

## 🧩 Servicios y timers

| Unidad | Descripción | Estado |
|---------|-------------|--------|
| `rspectrum7.service` | Procesa el día actual (parámetro `0`) | activo |
| `rspectrum7.timer` | Corre cada 10 min | activo |
| `rspectrum7-yesterday.service` | Procesa el día anterior (parámetro `1`) | activo |
| `rspectrum7-yesterday.timer` | Corre a las 00:45 | activo |

Verificar:
```bash
systemctl list-timers | grep rspectrum7
